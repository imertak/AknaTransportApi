using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// İş dökümü raporları. Varsayılan kapsam yalnızca tamamlanan işlerdir (scope=Completed);
/// scope=NotCancelled veya scope=All ile diğer durumlar dahil edilebilir.
/// </summary>
[Route("api/reports")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class ReportsController(IReportService reportService) : ApiControllerBase
{
    /// <summary>Örnek: /api/reports/jobs/pdf?startDate=2026-10-19&amp;endDate=2026-10-22</summary>
    [HttpGet("jobs/pdf")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> JobsPdf([FromQuery] JobReportQuery query, CancellationToken ct) =>
        Pdf(await reportService.GetJobsPdfAsync(query, ct));

    [HttpGet("jobs/summary")]
    public async Task<ActionResult<BaseResponse<JobReportSummaryDto>>> Summary([FromQuery] JobReportQuery query, CancellationToken ct) =>
        Success(await reportService.GetSummaryAsync(query, ct));

    [HttpGet("jobs")]
    public async Task<ActionResult<BaseResponse<JobReportDto>>> Jobs([FromQuery] JobReportPagedQuery query, CancellationToken ct) =>
        Success(await reportService.GetJobsAsync(query, ct));
}
