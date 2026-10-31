using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Reports;

public sealed class ReportService(
    IRepository<TransportJob> jobs,
    IRepository<Customer> customers,
    IJobReportPdfGenerator pdfGenerator,
    TimeProvider clock) : IReportService
{
    /// <summary>PDF için üst sınır; daha büyük aralıklar daraltılmalıdır.</summary>
    public const int MaxPdfRows = 10_000;

    public async Task<JobReportDto> GetJobsAsync(JobReportPagedQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var totals = await q.GroupBy(_ => 1).Select(g => new { Count = g.Count(), Amount = g.Sum(j => j.Price) }).FirstOrDefaultAsync(ct);
        var items = await q.OrderBy(j => j.JobDate).ThenBy(j => j.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(j => new JobReportItemDto(j.Id, j.JobNumber, j.JobDate, j.CustomerId, j.Customer!.CompanyName, j.StoreName, j.VehicleType,
                j.Description, j.Price, j.Currency, j.Status, j.AssignedDriver != null ? j.AssignedDriver.FullName : null))
            .ToListAsync(ct);

        var paged = new PagedResult<JobReportItemDto> { Items = items, PageNumber = query.PageNumber, PageSize = query.PageSize, TotalCount = totals?.Count ?? 0 };
        return new JobReportDto(query.StartDate, query.EndDate, query.Scope, totals?.Count ?? 0, totals?.Amount ?? 0m, paged, clock.GetUtcNow().UtcDateTime);
    }

    public async Task<JobReportSummaryDto> GetSummaryAsync(JobReportQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var byCustomer = await q.GroupBy(j => new { j.CustomerId, j.Customer!.CompanyName })
            .Select(g => new { g.Key.CustomerId, g.Key.CompanyName, Count = g.Count(), Amount = g.Sum(j => j.Price) })
            .ToListAsync(ct);
        var byVehicle = await q.GroupBy(j => j.VehicleType).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(j => j.Price) }).ToListAsync(ct);
        var byStatus = await q.GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(j => j.Price) }).ToListAsync(ct);
        var byDay = await q.GroupBy(j => j.JobDate).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(j => j.Price) }).ToListAsync(ct);

        return new JobReportSummaryDto(
            query.StartDate, query.EndDate, query.Scope,
            byStatus.Sum(x => x.Count), byStatus.Sum(x => x.Amount),
            byCustomer.OrderByDescending(x => x.Amount).Select(x => new GroupTotalDto(x.CustomerId.ToString(), x.CompanyName, x.Count, x.Amount)).ToList(),
            byVehicle.OrderByDescending(x => x.Amount).Select(x => new GroupTotalDto(x.Key.ToString(), x.Key.ToTurkish(), x.Count, x.Amount)).ToList(),
            byStatus.OrderBy(x => x.Key).Select(x => new GroupTotalDto(x.Key.ToString(), x.Key.ToString(), x.Count, x.Amount)).ToList(),
            byDay.OrderBy(x => x.Key).Select(x => new GroupTotalDto(x.Key.ToString("yyyy-MM-dd"), x.Key.ToString("dd.MM.yyyy"), x.Count, x.Amount)).ToList());
    }

    public async Task<FileDownload> GetJobsPdfAsync(JobReportQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        var count = await q.CountAsync(ct);
        if (count > MaxPdfRows) throw new BusinessValidationException($"Rapor {count} iş içeriyor. PDF için en fazla {MaxPdfRows} iş desteklenir; tarih aralığını daraltın.");

        var rows = await q.OrderBy(j => j.JobDate).ThenBy(j => j.CreatedAt)
            .Select(j => new JobReportRow(j.JobDate, j.StoreName, j.VehicleType, j.Description, j.Price, j.Customer!.CompanyName))
            .ToListAsync(ct);

        string? customerName = null;
        if (query.CustomerId is { } customerId)
            customerName = await customers.QueryAsNoTracking().Where(c => c.Id == customerId).Select(c => c.CompanyName).FirstOrDefaultAsync(ct);

        var subtitle = query.Scope switch
        {
            ReportStatusScope.Completed => null,
            ReportStatusScope.NotCancelled => "İptal edilenler hariç tüm işler",
            _ => "Tüm durumlar",
        };

        var document = new JobReportDocument("NAKLİYE İŞLERİ DÖKÜMÜ", query.StartDate, query.EndDate, rows, rows.Sum(r => r.Price), Money.DefaultCurrency,
            clock.GetUtcNow().UtcDateTime, customerName, subtitle);
        var pdf = pdfGenerator.Generate(document);

        var range = (query.StartDate, query.EndDate) switch
        {
            ({ } s, { } e) => $"{s:yyyyMMdd}-{e:yyyyMMdd}",
            ({ } s, null) => $"{s:yyyyMMdd}-",
            (null, { } e) => $"-{e:yyyyMMdd}",
            _ => "tum-zamanlar",
        };
        return new FileDownload(pdf, $"akna-is-dokumu-{range}.pdf");
    }

    private IQueryable<TransportJob> Filter(JobReportQuery query)
    {
        var q = jobs.QueryAsNoTracking();
        if (query.StartDate is { } start) q = q.Where(j => j.JobDate >= start);
        if (query.EndDate is { } end) q = q.Where(j => j.JobDate <= end);
        if (query.CustomerId is { } customerId) q = q.Where(j => j.CustomerId == customerId);
        if (query.DriverId is { } driverId) q = q.Where(j => j.AssignedDriverId == driverId);
        if (query.VehicleType is { } vehicle) q = q.Where(j => j.VehicleType == vehicle);
        if (query.StoreName.TrimOrNull() is { } store)
        {
            var s = store.ToLower();
            q = q.Where(j => j.StoreName.ToLower().Contains(s));
        }
        return query.Scope switch
        {
            ReportStatusScope.Completed => q.Where(j => j.Status == JobStatus.Completed),
            ReportStatusScope.NotCancelled => q.Where(j => j.Status != JobStatus.Cancelled),
            _ => q,
        };
    }
}
