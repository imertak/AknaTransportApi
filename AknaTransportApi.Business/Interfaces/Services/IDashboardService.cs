using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DailyJobCountDto>> GetDailyJobsAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int months, CancellationToken ct);

    /// <summary>Verilen ayın (varsayılan: içinde bulunulan ay) nakliye geliri.</summary>
    Task<MonthRevenueSummaryDto> GetRevenueAsync(int? year, int? month, CancellationToken ct);
    Task<IReadOnlyList<JobDto>> GetRecentJobsAsync(int count, CancellationToken ct);
    Task<IReadOnlyList<ActiveDriverDto>> GetActiveDriversAsync(CancellationToken ct);
}
