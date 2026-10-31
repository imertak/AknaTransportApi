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

    Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<EArchiveStatusResult> GetStatusAsync(string reference, CancellationToken cancellationToken = default);
    Task<byte[]> GetPdfAsync(string reference, CancellationToken cancellationToken = default);
    Task<EArchiveCancelResult> CancelAsync(string reference, string reason, CancellationToken cancellationToken = default);
}
