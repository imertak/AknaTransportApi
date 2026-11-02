namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>HTML belgeyi PDF'e çevirir (sunucudaki Chrome/Edge ile). GİB e-Arşiv portalının fatura görüntüsü bu yolla PDF olur.</summary>
public interface IHtmlPdfRenderer
{
    /// <summary>Sunucuda kullanılabilir bir tarayıcı bulunduysa true.</summary>
    bool IsAvailable { get; }

    Task<byte[]> RenderAsync(string html, CancellationToken ct);
}
