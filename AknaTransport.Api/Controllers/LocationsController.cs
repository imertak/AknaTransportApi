using AknaTransport.Api.Infrastructure;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Locations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AknaTransport.Api.Controllers;

[Route("api/locations")]
[Authorize]
public sealed class LocationsController(ILocationService locationService) : ApiControllerBase
{
    /// <summary>
    /// Şoför cihazından konum gönderimi. Yalnızca işe atanmış kişi ve yalnızca aktif (OnTheWay, Loading,
    /// Delivering) işlerde kabul edilir. Anlamsız (yakın/sık) konumlar kaydedilmez; çevrimdışı biriken
    /// konumlar "points" dizisiyle toplu gönderilebilir.
    /// </summary>
    [HttpPost("update")]
    [EnableRateLimiting(RateLimitPolicies.Location)]
    public async Task<ActionResult<BaseResponse<LocationUpdateResult>>> Update(UpdateLocationRequest request, CancellationToken ct) =>
        Success(await locationService.UpdateAsync(request, ct));

    [HttpGet("jobs/{jobId:guid}/latest")]
    public async Task<ActionResult<BaseResponse<LocationDto>>> Latest(Guid jobId, CancellationToken ct) =>
        Success(await locationService.GetLatestAsync(jobId, ct));

    /// <summary>Sayfalı konum geçmişi (varsayılan 100, en fazla 500 kayıt). from/to UTC.</summary>
    [HttpGet("jobs/{jobId:guid}/history")]
    public async Task<ActionResult<BaseResponse<PagedResult<LocationDto>>>> History(Guid jobId, [FromQuery] LocationHistoryQuery query, CancellationToken ct) =>
        Success(await locationService.GetHistoryAsync(jobId, query, ct));

    /// <summary>Aktif işler ve son konumları. Yönetici tüm işleri, şoför yalnızca kendi işlerini görür.</summary>
    [HttpGet("active-jobs")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<ActiveJobLocationDto>>>> ActiveJobs(CancellationToken ct) =>
        Success(await locationService.GetActiveJobsAsync(ct));
}

/// <summary>Müşteri takip bağlantısı (oturum gerektirmez; yalnızca ilgili işin durum ve konumunu gösterir).</summary>
[Route("api/tracking")]
public sealed class TrackingController(ILocationService locationService) : ApiControllerBase
{
    [HttpGet("{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PublicTracking)]
    public async Task<ActionResult<BaseResponse<PublicTrackingDto>>> Get(string token, CancellationToken ct)
    {
        if (token.Length is < 20 or > 200) return NotFound(BaseResponse.Fail("Takip bağlantısı geçersiz."));
        return Success(await locationService.GetPublicTrackingAsync(token, ct));
    }
}
