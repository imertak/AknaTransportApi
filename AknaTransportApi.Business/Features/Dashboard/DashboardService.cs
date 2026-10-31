using AknaTransport.Business.Features.Jobs;
using AknaTransport.Business.Features.Locations;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AknaTransport.Business.Features.Dashboard;

public sealed record DashboardSummaryDto(
    int TotalJobs,
    int PendingJobs,
    int AssignedJobs,
    // Kabul edilmiş, yolda, yüklemede ve teslimatta olan işler.
    int ActiveJobs,
    int CompletedJobs,
    int CancelledJobs,
    // Tamamlanan işlerin toplam nakliye tutarı.
    decimal TotalRevenue,
    // Bu ay tamamlanan işlerin toplam tutarı (iş tarihine göre).
    decimal MonthlyRevenue,
    int TotalCustomers,
    // Şu anda aktif bir işi bulunan şoför sayısı.
    int ActiveDrivers,
    DateOnly Today);

public sealed record DailyJobCountDto(DateOnly Date, int Total, int Completed, decimal CompletedAmount);

public sealed record MonthlyRevenueDto(int Year, int Month, decimal Revenue, int CompletedJobs);

public sealed record ActiveDriverDto(
    Guid DriverId,
    string FullName,
    string PhoneNumber,
    UserRole Role,
    Guid JobId,
    string JobNumber,
    JobStatus JobStatus,
    string StoreName,
    LocationDto? LastLocation);

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DailyJobCountDto>> GetDailyJobsAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int months, CancellationToken ct);
    Task<IReadOnlyList<JobDto>> GetRecentJobsAsync(int count, CancellationToken ct);
    Task<IReadOnlyList<ActiveDriverDto>> GetActiveDriversAsync(CancellationToken ct);
}

/// <summary>Tüm istatistikler veritabanında GROUP BY / SUM ile hesaplanır; kayıtlar belleğe çekilmez.</summary>
public sealed class DashboardService(
    IRepository<TransportJob> jobs,
    IRepository<Customer> customers,
    IRepository<JobLatestLocation> latestLocations,
    IBusinessCalendar calendar) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        var today = calendar.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var byStatus = await jobs.QueryAsNoTracking().GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(j => j.Price) })
            .ToListAsync(ct);

        var monthly = await jobs.QueryAsNoTracking()
            .Where(j => j.Status == JobStatus.Completed && j.JobDate >= monthStart && j.JobDate <= monthEnd)
            .SumAsync(j => (decimal?)j.Price, ct) ?? 0m;

        var inProgress = TransportJob.InProgressStatuses.ToList();
        var activeDrivers = await jobs.QueryAsNoTracking()
            .Where(j => inProgress.Contains(j.Status) && j.AssignedDriverId != null)
            .Select(j => j.AssignedDriverId).Distinct().CountAsync(ct);

        var totalCustomers = await customers.QueryAsNoTracking().CountAsync(c => c.IsActive, ct);

        int Count(params JobStatus[] statuses) => byStatus.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);

        return new DashboardSummaryDto(
            byStatus.Sum(x => x.Count),
            Count(JobStatus.Pending),
            Count(JobStatus.Assigned),
            Count(JobStatus.Accepted, JobStatus.OnTheWay, JobStatus.Loading, JobStatus.Delivering),
            Count(JobStatus.Completed),
            Count(JobStatus.Cancelled),
            byStatus.Where(x => x.Status == JobStatus.Completed).Sum(x => x.Amount),
            monthly,
            totalCustomers,
            activeDrivers,
            today);
    }

    public async Task<IReadOnlyList<DailyJobCountDto>> GetDailyJobsAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 366);
        var end = calendar.Today;
        var start = end.AddDays(-(days - 1));

        var rows = await jobs.QueryAsNoTracking()
            .Where(j => j.JobDate >= start && j.JobDate <= end && j.Status != JobStatus.Cancelled)
            .GroupBy(j => j.JobDate)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                Completed = g.Count(j => j.Status == JobStatus.Completed),
                Amount = g.Where(j => j.Status == JobStatus.Completed).Sum(j => (decimal?)j.Price) ?? 0m,
            })
            .ToListAsync(ct);

        var map = rows.ToDictionary(r => r.Date);
        return Enumerable.Range(0, days).Select(i => start.AddDays(i))
            .Select(d => map.TryGetValue(d, out var r) ? new DailyJobCountDto(d, r.Total, r.Completed, r.Amount) : new DailyJobCountDto(d, 0, 0, 0m))
            .ToList();
    }

    public async Task<IReadOnlyList<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int months, CancellationToken ct)
    {
        months = Math.Clamp(months, 1, 36);
        var today = calendar.Today;
        var firstMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var rows = await jobs.QueryAsNoTracking()
            .Where(j => j.Status == JobStatus.Completed && j.JobDate >= firstMonth)
            .GroupBy(j => new { j.JobDate.Year, j.JobDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(j => j.Price), Count = g.Count() })
            .ToListAsync(ct);

        var map = rows.ToDictionary(r => (r.Year, r.Month));
        return Enumerable.Range(0, months).Select(i => firstMonth.AddMonths(i))
            .Select(m => map.TryGetValue((m.Year, m.Month), out var r)
                ? new MonthlyRevenueDto(m.Year, m.Month, r.Revenue, r.Count)
                : new MonthlyRevenueDto(m.Year, m.Month, 0m, 0))
            .ToList();
    }

    public async Task<IReadOnlyList<JobDto>> GetRecentJobsAsync(int count, CancellationToken ct) =>
        await jobs.QueryAsNoTracking().OrderByDescending(j => j.CreatedAt).Take(Math.Clamp(count, 1, 50))
            .Select(JobMappings.Projection).ToListAsync(ct);

    public async Task<IReadOnlyList<ActiveDriverDto>> GetActiveDriversAsync(CancellationToken ct)
    {
        var inProgress = TransportJob.InProgressStatuses.ToList();
        var rows = await (from j in jobs.QueryAsNoTracking()
                          where inProgress.Contains(j.Status) && j.AssignedDriverId != null
                          join l in latestLocations.QueryAsNoTracking() on j.Id equals l.JobId into lj
                          from l in lj.DefaultIfEmpty()
                          orderby j.AssignedDriver!.FullName, j.JobDate
                          select new
                          {
                              DriverId = j.AssignedDriverId!.Value,
                              j.AssignedDriver!.FullName,
                              j.AssignedDriver.PhoneNumber,
                              j.AssignedDriver.Role,
                              JobId = j.Id,
                              j.JobNumber,
                              j.Status,
                              j.StoreName,
                              Location = l,
                          }).ToListAsync(ct);

        return rows.Select(r => new ActiveDriverDto(r.DriverId, r.FullName, r.PhoneNumber, r.Role, r.JobId, r.JobNumber, r.Status, r.StoreName,
            r.Location is null ? null : new LocationDto(r.Location.JobId, r.Location.DriverId, r.Location.Latitude, r.Location.Longitude,
                r.Location.Accuracy, r.Location.Speed, r.Location.Heading, r.Location.RecordedAt))).ToList();
    }
}
