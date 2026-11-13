using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// Nakliye teklifleri (yalnızca yönetici). Tutarlar sunucuda kalemlerden hesaplanır. Kabul edilen teklif
/// "convert-to-job" ile tek transaction içinde nakliye işine dönüştürülür; aynı tekliften ikinci iş oluşturulamaz.
/// </summary>
[Route("api/transport-offers")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class TransportOffersController(ITransportOfferService offers) : ApiControllerBase
{
    /// <summary>Filtreler: search (teklif no, müşteri, yükleme/boşaltma), customerId, status (çoklu), startDate, endDate (teklif tarihi).</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<TransportOfferListItemDto>>>> List([FromQuery] TransportOfferQuery query, CancellationToken ct) =>
        Success(await offers.ListAsync(query, ct));

    /// <summary>İstatistik kartları: toplam, taslak, bekleyen, kabul, ret, süresi dolan, iptal (durum filtresi uygulanmaz).</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<BaseResponse<TransportOfferStatsDto>>> Stats([FromQuery] TransportOfferQuery query, CancellationToken ct) =>
        Success(await offers.StatsAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await offers.GetAsync(id, ct));

    /// <summary>Taslak teklif oluşturur. customerId boşsa müşteri bilgileriyle yeni müşteri kaydı açılır.</summary>
    [HttpPost]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Create(TransportOfferRequest request, CancellationToken ct)
    {
        var offer = await offers.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = offer.Id }, offer, $"{offer.OfferNumber} numaralı teklif oluşturuldu.");
    }

    /// <summary>Yalnızca taslak veya yanıt bekleyen (süresi dolmuş dahil) teklif düzenlenebilir; kabul edilen teklif kilitlidir.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Update(Guid id, TransportOfferRequest request, CancellationToken ct) =>
        Success(await offers.UpdateAsync(id, request, ct), "Teklif güncellendi.");

    [HttpPost("{id:guid}/copy")]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Copy(Guid id, CancellationToken ct)
    {
        var offer = await offers.CopyAsync(id, ct);
        return Created(nameof(Get), new { id = offer.Id }, offer, $"Teklif kopyalandı: {offer.OfferNumber}.");
    }

    /// <summary>Taslak teklifi "müşteriye gönderildi, yanıt bekleniyor" olarak işaretler.</summary>
    [HttpPost("{id:guid}/send")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Send(Guid id, CancellationToken ct) =>
        Success(await offers.MarkSentAsync(id, ct), "Teklif gönderildi olarak işaretlendi.");

    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Accept(Guid id, [FromBody] OfferStatusRequest? request, CancellationToken ct) =>
        Success(await offers.AcceptAsync(id, request ?? new(), ct), "Teklif kabul edildi.");

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Reject(Guid id, [FromBody] OfferStatusRequest? request, CancellationToken ct) =>
        Success(await offers.RejectAsync(id, request ?? new(), ct), "Teklif reddedildi olarak işaretlendi.");

    /// <summary>Teklifi iptal eder (neden zorunlu). İşe dönüştürülmüş teklif iptal edilemez.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<TransportOfferDetailDto>>> Cancel(Guid id, [FromBody] OfferStatusRequest? request, CancellationToken ct) =>
        Success(await offers.CancelAsync(id, request ?? new(), ct), "Teklif iptal edildi.");

    /// <summary>Mantıksal silme. Kabul edilmiş veya işe dönüştürülmüş teklif silinemez.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse>> Delete(Guid id, CancellationToken ct)
    {
        await offers.DeleteAsync(id, ct);
        return Done("Teklif silindi.");
    }

    /// <summary>Kurumsal teklif PDF'i (AKNA_Teklif_AKT-2026-000001.pdf). Her istekte güncel veriden üretilir, sunucuda saklanmaz.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct) => Pdf(await offers.GetPdfAsync(id, ct));

    /// <summary>Kabul edilmiş teklifi nakliye işine dönüştürür. Müşteri, güzergâh, araç ve fiyat bilgileri aktarılır.</summary>
    [HttpPost("{id:guid}/convert-to-job")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<ConvertOfferResultDto>>> ConvertToJob(Guid id, CancellationToken ct)
    {
        var result = await offers.ConvertToJobAsync(id, ct);
        return Success(result, $"{result.JobNumber} numaralı nakliye işi oluşturuldu.");
    }
}
