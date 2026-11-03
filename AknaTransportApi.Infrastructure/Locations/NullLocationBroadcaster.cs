namespace AknaTransportApi.Infrastructure.Locations;

/// <summary>
/// Varsayılan yayıncı: gerçek zamanlı yayın yapmaz. API katmanı bunu SignalR uygulamasıyla
/// (<c>SignalRLocationBroadcaster</c>) değiştirir; servis kodu değişmez.
/// </summary>
public sealed class NullLocationBroadcaster : ILocationBroadcaster
{
    public Task PublishAsync(LocationDto location, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishJobChangedAsync(JobChangedDto change, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
