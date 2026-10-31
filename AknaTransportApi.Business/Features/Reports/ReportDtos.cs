using AknaTransport.Business.Common;
using AknaTransport.Domain.Enums;
using FluentValidation;

namespace AknaTransport.Business.Features.Reports;

/// <summary>Rapora dahil edilecek iş durumları.</summary>
public enum ReportStatusScope
{
    /// <summary>Yalnızca tamamlanan işler (varsayılan).</summary>
    Completed = 0,
    /// <summary>İptal edilenler hariç tüm işler.</summary>
    NotCancelled = 1,
    /// <summary>Tüm durumlar.</summary>
    All = 2,
}

public class JobReportQuery
{
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? DriverId { get; set; }
    public string? StoreName { get; set; }
    public VehicleType? VehicleType { get; set; }
    public ReportStatusScope Scope { get; set; } = ReportStatusScope.Completed;
}

public sealed class JobReportPagedQuery : JobReportQuery
{
    private int _pageNumber = 1;
    private int _pageSize = 50;
    public int PageNumber { get => _pageNumber; set => _pageNumber = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; set => _pageSize = value < 1 ? 50 : Math.Min(value, 500); }
}

public sealed record JobReportItemDto(
    Guid Id,
    string JobNumber,
    DateOnly JobDate,
    Guid CustomerId,
    string CustomerName,
    string StoreName,
    VehicleType VehicleType,
    string Description,
    decimal Price,
    string Currency,
    JobStatus Status,
    string? DriverName);

public sealed record JobReportDto(
    DateOnly? StartDate,
    DateOnly? EndDate,
    ReportStatusScope Scope,
    int TotalCount,
    decimal TotalAmount,
    PagedResult<JobReportItemDto> Jobs,
    DateTime GeneratedAt);

public sealed record GroupTotalDto(string Key, string Label, int Count, decimal Amount);

public sealed record JobReportSummaryDto(
    DateOnly? StartDate,
    DateOnly? EndDate,
    ReportStatusScope Scope,
    int TotalCount,
    decimal TotalAmount,
    IReadOnlyList<GroupTotalDto> ByCustomer,
    IReadOnlyList<GroupTotalDto> ByVehicleType,
    IReadOnlyList<GroupTotalDto> ByStatus,
    IReadOnlyList<GroupTotalDto> ByDay);

public class JobReportQueryValidator<T> : AbstractValidator<T> where T : JobReportQuery
{
    public JobReportQueryValidator()
    {
        RuleFor(x => x).Must(x => x.StartDate is null || x.EndDate is null || x.StartDate <= x.EndDate)
            .WithName("startDate").WithMessage("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        RuleFor(x => x).Must(x => x.StartDate is null || x.EndDate is null || x.EndDate.Value.DayNumber - x.StartDate.Value.DayNumber <= 731)
            .WithName("endDate").WithMessage("Rapor aralığı en fazla 2 yıl olabilir.");
        RuleFor(x => x.Scope).IsInEnum();
    }
}

public sealed class JobReportQueryValidator : JobReportQueryValidator<JobReportQuery>;

public sealed class JobReportPagedQueryValidator : JobReportQueryValidator<JobReportPagedQuery>;
