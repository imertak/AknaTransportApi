using AknaTransport.Api.Infrastructure;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Dashboard;
using AknaTransport.Business.Features.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransport.Api.Controllers;

[Route("api/dashboard")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class DashboardController(IDashboardService dashboardService) : ApiControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<BaseResponse<DashboardSummaryDto>>> Summary(CancellationToken ct) =>
        Success(await dashboardService.GetSummaryAsync(ct));

    /// <summary>Son N günün günlük iş sayıları (varsayılan 30).</summary>
    [HttpGet("daily-jobs")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<DailyJobCountDto>>>> DailyJobs([FromQuery] int days = 30, CancellationToken ct = default) =>
        Success(await dashboardService.GetDailyJobsAsync(days, ct));

    /// <summary>Son N ayın gelir grafiği (varsayılan 12).</summary>
    [HttpGet("monthly-revenue")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<MonthlyRevenueDto>>>> MonthlyRevenue([FromQuery] int months = 12, CancellationToken ct = default) =>
        Success(await dashboardService.GetMonthlyRevenueAsync(months, ct));

    [HttpGet("recent-jobs")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<JobDto>>>> RecentJobs([FromQuery] int count = 10, CancellationToken ct = default) =>
        Success(await dashboardService.GetRecentJobsAsync(count, ct));

    /// <summary>Şu anda aktif işi olan şoförler ve son konumları.</summary>
    [HttpGet("active-drivers")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<ActiveDriverDto>>>> ActiveDrivers(CancellationToken ct) =>
        Success(await dashboardService.GetActiveDriversAsync(ct));
}
