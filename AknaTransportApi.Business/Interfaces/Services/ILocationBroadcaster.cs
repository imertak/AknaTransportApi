namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>
/// Gerçek zamanlı yayın noktası. API katmanı SignalR hub'ı (<c>/hubs/tracking</c>) üzerinden uygular;
/// arka plan işleri ve testler için varsayılan uygulama hiçbir şey yapmaz. Yayın hatası asıl işlemi bozmaz.
/// </summary>
public interface ILocationBroadcaster
{
    /// <summary>Kaydedilen son konumu işin yetkili izleyicilerine (yöneticiler ve işi izleyenler) iletir.</summary>
    Task PublishAsync(LocationDto location, CancellationToken cancellationToken = default);

    /// <summary>
    /// İşin durumu, şoförü/aracı değiştiğinde veya iş kaldırıldığında istemcilere "yeniden yükle" ipucu gönderir.
    /// Veri içermez; istemci güncel hali yetkili uç noktalardan okur.
    /// </summary>
    Task PublishJobChangedAsync(JobChangedDto change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
