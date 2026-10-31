namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>
/// e-Arşiv entegratörü soyutlaması. Somut sağlayıcılar (Infrastructure) bu arayüzü
/// adapter olarak uygular. Sağlayıcı yapılandırılmamışsa <see cref="IsConfigured"/> false döner
/// ve hiçbir fatura Issued durumuna geçirilmez.
/// </summary>
public interface IEArchiveInvoiceService
{
    bool IsConfigured { get; }

    /// <summary>Yapılandırılmış sağlayıcının adı (ör. "Uyumsoft"). Yapılandırılmamışsa null.</summary>
    string? ProviderName { get; }

    /// <summary>
    /// true ise sağlayıcı faturayı taslak olarak alır ve imza için SMS doğrulaması ister (GİB e-Arşiv portalı).
    /// Bu durumda SendInvoiceAsync Processing döner; fatura ancak SMS kodu doğrulanınca kesilmiş olur.
    /// </summary>
    bool SupportsSmsSigning { get; }

    /// <summary>Sağlayıcı resmi PDF üretebiliyor mu. GİB portalı yalnızca HTML görüntü verir.</summary>
    bool SupportsPdf { get; }

    Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<EArchiveStatusResult> GetStatusAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default);
    Task<byte[]> GetPdfAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default);
    Task<EArchiveCancelResult> CancelAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken = default);

    /// <summary>İmza SMS'ini yetkilinin kayıtlı telefonuna gönderir.</summary>
    Task<EArchiveSmsChallenge> StartSmsSigningAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// SMS kodunu doğrular ve faturayı imzalar. Yalnızca sağlayıcı imzayı onaylarsa Issued döner.
    /// </summary>
    Task<EArchiveStatusResult> CompleteSmsSigningAsync(EArchiveDocumentKey key, string operationId, string code, CancellationToken cancellationToken = default);

    /// <summary>Sağlayıcıdaki imzalanmamış taslağı siler.</summary>
    Task<EArchiveCancelResult> DiscardDraftAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken = default);

    /// <summary>Sağlayıcının belge görüntüsü (HTML). Desteklenmiyorsa null.</summary>
    Task<string?> GetHtmlAsync(EArchiveDocumentKey key, bool signed, CancellationToken cancellationToken = default);
}
