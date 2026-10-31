using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.EArchive;

/// <summary>
/// Somut e-Arşiv entegratörü. Her sağlayıcı (Uyumsoft, Logo, Sovos/Foriba, QNB eFinans vb.)
/// kendi API dokümantasyonuna göre bu arayüzü uygular. <see cref="HttpEArchiveProviderAdapterBase"/>
/// HTTP tabanlı sağlayıcılar için ortak altyapıyı sağlar.
/// </summary>
public interface IEArchiveProviderAdapter
{
    string Name { get; }
    Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken);
    Task<EArchiveStatusResult> GetStatusAsync(string reference, CancellationToken cancellationToken);
    Task<byte[]> GetPdfAsync(string reference, CancellationToken cancellationToken);
    Task<EArchiveCancelResult> CancelAsync(string reference, string reason, CancellationToken cancellationToken);
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

    public Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken = default) =>
        Adapter.SendInvoiceAsync(request, cancellationToken);

    public Task<EArchiveStatusResult> GetStatusAsync(string reference, CancellationToken cancellationToken = default) =>
        Adapter.GetStatusAsync(reference, cancellationToken);

    public Task<byte[]> GetPdfAsync(string reference, CancellationToken cancellationToken = default) =>
        Adapter.GetPdfAsync(reference, cancellationToken);

    public Task<EArchiveCancelResult> CancelAsync(string reference, string reason, CancellationToken cancellationToken = default) =>
        Adapter.CancelAsync(reference, reason, cancellationToken);

    private IEArchiveProviderAdapter Adapter =>
        _adapter ?? throw new InvalidOperationException("e-Arşiv sağlayıcısı yapılandırılmamış (EArchive:Provider).");

    private static IEArchiveProviderAdapter? Resolve(IEnumerable<IEArchiveProviderAdapter> adapters, EArchiveOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Provider)) return null;
        var adapter = adapters.FirstOrDefault(a => string.Equals(a.Name, options.Provider, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            logger.LogError("EArchive:Provider '{Provider}' olarak ayarlı ancak bu ada sahip bir adaptör kayıtlı değil. e-Arşiv devre dışı.", options.Provider);
        return adapter;
    }
}
