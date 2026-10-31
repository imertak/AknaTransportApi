using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// Fatura yönetimi. Faturalar taslak (Draft) olarak oluşturulur; yalnızca gerçek e-Arşiv sağlayıcısından
/// başarılı yanıt alındığında Issued olur ve müşteriye otomatik e-posta kuyruğa alınır.
/// </summary>
[Route("api/invoices")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class InvoicesController(IInvoiceService invoiceService) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<InvoiceListItemDto>>>> List([FromQuery] InvoiceQuery query, CancellationToken ct) =>
        Success(await invoiceService.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await invoiceService.GetAsync(id, ct));

    /// <summary>Tamamlanmış, faturalanmamış ve aynı müşteriye ait işlerden taslak fatura oluşturur.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await invoiceService.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = invoice.Id }, invoice, "Taslak fatura oluşturuldu.");
    }

    /// <summary>Yalnızca Draft veya Failed faturalar güncellenebilir.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Update(Guid id, UpdateInvoiceRequest request, CancellationToken ct) =>
        Success(await invoiceService.UpdateAsync(id, request, ct), "Fatura güncellendi.");

    /// <summary>
    /// Faturayı e-Arşiv sağlayıcısına gönderir. Sağlayıcı yapılandırılmamışsa 503 döner ve fatura taslak kalır.
    /// Başarılı olursa fatura Issued olur ve müşteri e-postası (fatura + iş dökümü PDF) kuyruğa alınır.
    /// </summary>
    [HttpPost("{id:guid}/issue")]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Issue(Guid id, CancellationToken ct)
    {
        var invoice = await invoiceService.IssueAsync(id, ct);
        var message = invoice.Status switch
        {
            Domain.Enums.InvoiceStatus.Issued => "Fatura kesildi; müşteriye e-posta gönderimi kuyruğa alındı.",
            Domain.Enums.InvoiceStatus.Pending when invoice.RequiresSmsSignature =>
                "Fatura GİB e-Arşiv portalında taslak olarak oluşturuldu. Kesilmesi için SMS koduyla imzalayın.",
            Domain.Enums.InvoiceStatus.Pending => "Fatura sağlayıcıya iletildi, sonuç bekleniyor.",
            _ => $"Fatura kesilemedi: {invoice.LastError}",
        };
        return Success(invoice, message);
    }

    /// <summary>GİB portalındaki taslak için imza SMS'ini yetkilinin kayıtlı telefonuna gönderir.</summary>
    [HttpPost("{id:guid}/earchive/sms")]
    public async Task<ActionResult<BaseResponse<EArchiveSmsChallengeDto>>> SendSignSms(Guid id, CancellationToken ct)
    {
        var challenge = await invoiceService.SendSignSmsAsync(id, ct);
        return Success(challenge, challenge.MaskedPhone is null ? "SMS kodu gönderildi." : $"SMS kodu {challenge.MaskedPhone} numarasına gönderildi.");
    }

    /// <summary>SMS kodunu doğrular ve faturayı imzalar. GİB imzayı onaylamazsa fatura kesilmiş sayılmaz.</summary>
    [HttpPost("{id:guid}/earchive/sign")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Sign(Guid id, SignInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await invoiceService.SignAsync(id, request, ct);
        return Success(invoice, invoice.Status == Domain.Enums.InvoiceStatus.Issued
            ? "Fatura imzalandı ve kesildi; müşteriye e-posta gönderimi kuyruğa alındı."
            : $"Fatura kesilemedi: {invoice.LastError}");
    }

    /// <summary>GİB portalındaki imzalanmamış taslağı siler; fatura yeniden düzenlenebilir taslak olur.</summary>
    [HttpPost("{id:guid}/earchive/discard")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> DiscardEArchiveDraft(Guid id, CancellationToken ct) =>
        Success(await invoiceService.DiscardEArchiveDraftAsync(id, ct), "Portal taslağı silindi; fatura yeniden taslak.");

    /// <summary>Sağlayıcının belge görüntüsü (GİB portalı HTML).</summary>
    [HttpGet("{id:guid}/earchive/view")]
    [Produces("text/html", "application/json")]
    public async Task<IActionResult> EArchiveView(Guid id, CancellationToken ct)
    {
        var file = await invoiceService.GetEArchiveViewAsync(id, ct);
        return File(file.Content, file.ContentType);
    }

    /// <summary>KDV tevkifat kodları (ör. 624 Yük taşımacılığı 2/10).</summary>
    [HttpGet("withholding-types")]
    public ActionResult<BaseResponse<IReadOnlyList<WithholdingTypeDto>>> WithholdingTypes() =>
        Success(invoiceService.GetWithholdingTypes());

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> Cancel(Guid id, [FromBody] CancelInvoiceRequest? request, CancellationToken ct) =>
        Success(await invoiceService.CancelAsync(id, request ?? new(), ct), "Fatura iptal edildi.");

    /// <summary>Başarısız olmuş fatura e-postasını yeniden kuyruğa alır. Gönderilmiş e-posta tekrar gönderilmez.</summary>
    [HttpPost("{id:guid}/resend-email")]
    public async Task<ActionResult<BaseResponse<InvoiceDetailDto>>> ResendEmail(Guid id, CancellationToken ct) =>
        Success(await invoiceService.ResendEmailAsync(id, ct), "E-posta yeniden gönderim kuyruğuna alındı.");

    /// <summary>Kesilmiş faturada resmi e-Arşiv PDF'i; taslakta "resmi değildir" ibareli önizleme.</summary>
    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct) => Pdf(await invoiceService.GetPdfAsync(id, ct));

    /// <summary>Faturadaki işlerin nakliye dökümü PDF'i (adresler yükleme → teslimat sırasıyla).</summary>
    [HttpGet("{id:guid}/job-report/pdf")]
    [Produces("application/pdf", "application/json")]
    public async Task<IActionResult> JobReportPdf(Guid id, CancellationToken ct) => Pdf(await invoiceService.GetJobReportPdfAsync(id, ct));

    [HttpGet("{id:guid}/jobs")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<JobDto>>>> Jobs(Guid id, CancellationToken ct) =>
        Success(await invoiceService.GetJobsAsync(id, ct));
}
