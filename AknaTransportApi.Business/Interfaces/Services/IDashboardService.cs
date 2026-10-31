using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<DailyJobCountDto>> GetDailyJobsAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<MonthlyRevenueDto>> GetMonthlyRevenueAsync(int months, CancellationToken ct);
    Task<IReadOnlyList<JobDto>> GetRecentJobsAsync(int count, CancellationToken ct);
    Task<IReadOnlyList<ActiveDriverDto>> GetActiveDriversAsync(CancellationToken ct);
}
