using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.EArchive;

/// <summary>
/// Somut e-Arşiv entegratörü. Her sağlayıcı (GİB e-Arşiv portalı, Uyumsoft, Logo, Sovos vb.)
/// kendi API dokümantasyonuna göre bu arayüzü uygular.
/// </summary>
public interface IEArchiveProviderAdapter
{
    string Name { get; }
    Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken);
    Task<EArchiveStatusResult> GetStatusAsync(EArchiveDocumentKey key, CancellationToken cancellationToken);
    Task<byte[]> GetPdfAsync(EArchiveDocumentKey key, CancellationToken cancellationToken);
    Task<EArchiveCancelResult> CancelAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken);
}

/// <summary>Faturayı taslak olarak alıp SMS doğrulamasıyla imzalayan sağlayıcılar (GİB e-Arşiv portalı).</summary>
public interface IEArchiveSmsSigningAdapter : IEArchiveProviderAdapter
{
    Task<EArchiveSmsChallenge> StartSmsSigningAsync(EArchiveDocumentKey key, CancellationToken cancellationToken);
    Task<EArchiveStatusResult> CompleteSmsSigningAsync(EArchiveDocumentKey key, string operationId, string code, CancellationToken cancellationToken);
    Task<EArchiveCancelResult> DiscardDraftAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken);
    Task<string> GetHtmlAsync(EArchiveDocumentKey key, bool signed, CancellationToken cancellationToken);
}

/// <summary>
/// Sağlayıcıdan bağımsız e-Arşiv servisi: yapılandırmadaki Provider adına göre kayıtlı adaptörü seçer.
/// Adaptör yoksa <see cref="IsConfigured"/> false döner, hiçbir çağrı sahte başarı üretmez.
/// </summary>
public sealed class EArchiveInvoiceService(
    IEnumerable<IEArchiveProviderAdapter> adapters,
    IOptions<EArchiveOptions> options,
    ILogger<EArchiveInvoiceService> logger) : IEArchiveInvoiceService
{
    private readonly IEArchiveProviderAdapter? _adapter = Resolve(adapters, options.Value, logger);

    public bool IsConfigured => _adapter is not null;

    public string? ProviderName => _adapter?.Name;

    public bool SupportsSmsSigning => _adapter is IEArchiveSmsSigningAdapter;

    public bool SupportsPdf => _adapter is not null and not IEArchiveSmsSigningAdapter;

    public Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken = default) =>
        Adapter.SendInvoiceAsync(request, cancellationToken);

    public Task<EArchiveStatusResult> GetStatusAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default) =>
        Adapter.GetStatusAsync(key, cancellationToken);

    public Task<byte[]> GetPdfAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default) =>
        Adapter.GetPdfAsync(key, cancellationToken);

    public Task<EArchiveCancelResult> CancelAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken = default) =>
        Adapter.CancelAsync(key, reason, cancellationToken);

    public Task<EArchiveSmsChallenge> StartSmsSigningAsync(EArchiveDocumentKey key, CancellationToken cancellationToken = default) =>
        SmsAdapter.StartSmsSigningAsync(key, cancellationToken);

    public Task<EArchiveStatusResult> CompleteSmsSigningAsync(EArchiveDocumentKey key, string operationId, string code, CancellationToken cancellationToken = default) =>
        SmsAdapter.CompleteSmsSigningAsync(key, operationId, code, cancellationToken);

    public Task<EArchiveCancelResult> DiscardDraftAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken = default) =>
        SmsAdapter.DiscardDraftAsync(key, reason, cancellationToken);

    public async Task<string?> GetHtmlAsync(EArchiveDocumentKey key, bool signed, CancellationToken cancellationToken = default) =>
        _adapter is IEArchiveSmsSigningAdapter sms ? await sms.GetHtmlAsync(key, signed, cancellationToken) : null;

    private IEArchiveProviderAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("e-Arşiv sağlayıcısı yapılandırılmamış (EArchive:Provider).");

    private IEArchiveSmsSigningAdapter SmsAdapter =>
        Adapter as IEArchiveSmsSigningAdapter ?? throw new InvalidOperationException($"{Adapter.Name} sağlayıcısı SMS ile imzalamayı desteklemiyor.");

    private static IEArchiveProviderAdapter? Resolve(IEnumerable<IEArchiveProviderAdapter> adapters, EArchiveOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Provider)) return null;
        var adapter = adapters.FirstOrDefault(a => string.Equals(a.Name, options.Provider, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            logger.LogError("EArchive:Provider '{Provider}' olarak ayarlı ancak bu ada sahip bir adaptör kayıtlı değil. e-Arşiv devre dışı.", options.Provider);
        return adapter;
    }
}
