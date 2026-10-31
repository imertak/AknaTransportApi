using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IInvoiceService
{
    Task<PagedResult<InvoiceListItemDto>> ListAsync(InvoiceQuery query, CancellationToken ct);
    Task<InvoiceDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<JobDto>> GetJobsAsync(Guid id, CancellationToken ct);
    Task<InvoiceDetailDto> CreateAsync(CreateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailDto> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailDto> IssueAsync(Guid id, CancellationToken ct);

    /// <summary>GİB portalında bekleyen taslak için imza SMS'i gönderir.</summary>
    Task<EArchiveSmsChallengeDto> SendSignSmsAsync(Guid id, CancellationToken ct);

    /// <summary>SMS kodunu doğrular; GİB imzayı onaylarsa fatura Issued olur.</summary>
    Task<InvoiceDetailDto> SignAsync(Guid id, SignInvoiceRequest request, CancellationToken ct);

    /// <summary>Sağlayıcıdaki imzalanmamış taslağı siler ve faturayı düzenlenebilir taslağa döndürür.</summary>
    Task<InvoiceDetailDto> DiscardEArchiveDraftAsync(Guid id, CancellationToken ct);

    /// <summary>Sağlayıcının belge görüntüsü (GİB portalı HTML).</summary>
    Task<FileDownload> GetEArchiveViewAsync(Guid id, CancellationToken ct);

    IReadOnlyList<WithholdingTypeDto> GetWithholdingTypes();
    Task<InvoiceDetailDto> CancelAsync(Guid id, CancelInvoiceRequest request, CancellationToken ct);
    Task<InvoiceDetailDto> ResendEmailAsync(Guid id, CancellationToken ct);
    Task<FileDownload> GetPdfAsync(Guid id, CancellationToken ct);

    /// <summary>Gerçek e-Arşiv PDF'ini depodan veya sağlayıcıdan getirir (yalnızca Issued).</summary>
    Task<byte[]> GetOfficialPdfAsync(Guid id, CancellationToken ct);

    /// <summary>Faturadaki işlerin dökümü (müşteriye gönderilen rapor yalnızca bu işleri içerir).</summary>
    Task<JobReportDocument> BuildJobReportAsync(Guid id, CancellationToken ct);

    /// <summary>Faturaya dahil edilen işlerin nakliye dökümü PDF'i (resmi fatura değildir).</summary>
    Task<FileDownload> GetJobReportPdfAsync(Guid id, CancellationToken ct);

    /// <summary>Sağlayıcıda işlemde olan faturaların durumunu senkronize eder (arka plan).</summary>
    Task<int> SyncPendingAsync(CancellationToken ct);
}
