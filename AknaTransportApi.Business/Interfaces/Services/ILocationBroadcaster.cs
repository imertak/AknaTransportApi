namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>
/// Gerçek zamanlı konum yayını için genişleme noktası. Varsayılan uygulama hiçbir şey yapmaz;
/// SignalR hub eklendiğinde bu arayüz uygulanarak canlı yayın yapılabilir.
/// </summary>
public interface ILocationBroadcaster
{
    Task PublishAsync(LocationDto location, CancellationToken cancellationToken = default);
}
