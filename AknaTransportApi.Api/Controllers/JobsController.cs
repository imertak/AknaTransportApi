using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// Nakliye işleri. Yönetici tüm işleri yönetir; şoför (User) yalnızca kendisine atanmış işleri görür
/// ve durumlarını ilerletir. Yönetici işi kendisine atayıp şoför gibi tamamlayabilir.
/// </summary>
[Route("api/jobs")]
[Authorize]
public sealed class JobsController(IJobService jobService, ILocationService locationService) : ApiControllerBase
{
    /// <summary>İş listesi. Filtreler: startDate, endDate, customerId, driverId, status (çoklu), vehicleType, storeName, search, uninvoiced. Sıralama: sortBy=jobDate|price|createdAt|jobNumber|status|storeName, sortDirection=asc|desc.</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<JobDto>>>> List([FromQuery] JobQuery query, CancellationToken ct) =>
        Success(await jobService.ListAsync(query, ct));

    /// <summary>Oturumdaki kullanıcıya atanmış işler (yönetici için de kendi işleri).</summary>
    [HttpGet("my-jobs")]
    public async Task<ActionResult<BaseResponse<PagedResult<JobDto>>>> MyJobs([FromQuery] JobQuery query, CancellationToken ct) =>
        Success(await jobService.ListMyJobsAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await jobService.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<JobStatusHistoryDto>>>> History(Guid id, CancellationToken ct) =>
        Success(await jobService.GetHistoryAsync(id, ct));

    /// <summary>Yeni iş. Mevcut müşteri (customerId) veya yeni müşteri (newCustomer); şoför (assignedDriverId) veya assignToMe.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Create(CreateJobRequest request, CancellationToken ct)
    {
        var job = await jobService.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = job.Job.Id }, job, "İş oluşturuldu.");
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Update(Guid id, UpdateJobRequest request, CancellationToken ct) =>
        Success(await jobService.UpdateAsync(id, request, ct), "İş güncellendi.");

    /// <summary>Yalnızca başlamamış (Pending/Assigned) veya iptal edilmiş, faturalanmamış işler silinebilir.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse>> Delete(Guid id, CancellationToken ct)
    {
        await jobService.DeleteAsync(id, ct);
        return Done("İş silindi.");
    }

    /// <summary>Şoför atar; driverId null ise atama kaldırılır. Yönetici kendi Id'sini de verebilir.</summary>
    [HttpPatch("{id:guid}/assign")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Assign(Guid id, AssignJobRequest request, CancellationToken ct) =>
        Success(await jobService.AssignAsync(id, request, ct), "Şoför ataması güncellendi.");

    /// <summary>Yönetici işi kendi üzerine alır (Accepted).</summary>
    [HttpPatch("{id:guid}/claim")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Claim(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.ClaimAsync(id, request ?? new(), ct), "İş üstlenildi.");

    /// <summary>Assigned → Accepted</summary>
    [HttpPatch("{id:guid}/accept")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Accept(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.AcceptAsync(id, request ?? new(), ct), "İş kabul edildi.");

    /// <summary>Accepted → OnTheWay</summary>
    [HttpPatch("{id:guid}/start")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Start(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.StartAsync(id, request ?? new(), ct), "Yola çıkıldı.");

    /// <summary>OnTheWay → Loading</summary>
    [HttpPatch("{id:guid}/start-loading")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> StartLoading(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.StartLoadingAsync(id, request ?? new(), ct), "Yükleme başladı.");

    /// <summary>Loading → Delivering</summary>
    [HttpPatch("{id:guid}/start-delivery")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> StartDelivery(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.StartDeliveryAsync(id, request ?? new(), ct), "Teslimata çıkıldı.");

    /// <summary>Delivering → Completed</summary>
    [HttpPatch("{id:guid}/complete")]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Complete(Guid id, [FromBody] JobActionRequest? request, CancellationToken ct) =>
        Success(await jobService.CompleteAsync(id, request ?? new(), ct), "İş tamamlandı.");

    [HttpPatch("{id:guid}/cancel")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<JobDetailDto>>> Cancel(Guid id, [FromBody] CancelJobRequest? request, CancellationToken ct) =>
        Success(await jobService.CancelAsync(id, request ?? new(), ct), "İş iptal edildi.");

    /// <summary>Müşteriye verilecek salt okunur canlı takip bağlantısı oluşturur.</summary>
    [HttpPost("{id:guid}/tracking-links")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<TrackingTokenDto>>> CreateTrackingLink(Guid id, CancellationToken ct) =>
        Success(await locationService.CreateTrackingTokenAsync(id, ct), "Takip bağlantısı oluşturuldu.");

    /// <summary>İşin tüm takip bağlantılarını iptal eder.</summary>
    [HttpDelete("{id:guid}/tracking-links")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse>> RevokeTrackingLinks(Guid id, CancellationToken ct)
    {
        await locationService.RevokeTrackingTokensAsync(id, ct);
        return Done("Takip bağlantıları iptal edildi.");
    }
}
