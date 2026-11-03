using Microsoft.AspNetCore.SignalR;

namespace AknaTransportApi.Api.Realtime;

/// <summary>
/// <see cref="ILocationBroadcaster"/>'ın SignalR uygulaması. Konumlar yalnızca yöneticilere ve işi
/// (yetki kontrolünden geçerek) izleyen bağlantılara, iş değişikliği ipuçları yöneticilere ve işin
/// şoförlerine gider. Başka kullanıcıların konumu hiçbir zaman genel bir gruba yayınlanmaz.
/// </summary>
public sealed class SignalRLocationBroadcaster(IHubContext<TrackingHub> hub, ILogger<SignalRLocationBroadcaster> logger) : ILocationBroadcaster
{
    public async Task PublishAsync(LocationDto location, CancellationToken cancellationToken = default)
    {
        try
        {
            await hub.Clients.Groups([TrackingHub.AdminsGroup, TrackingHub.JobGroup(location.JobId)])
                .SendAsync(TrackingHub.LocationUpdatedEvent, location, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Canlı konum yayınlanamadı. JobId: {JobId}", location.JobId);
        }
    }

    public async Task PublishJobChangedAsync(JobChangedDto change, CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = new List<string> { TrackingHub.AdminsGroup, TrackingHub.JobGroup(change.JobId) };
            groups.AddRange(change.DriverIds.Select(TrackingHub.UserGroup));
            await hub.Clients.Groups(groups).SendAsync(TrackingHub.JobChangedEvent, change, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "İş değişikliği yayınlanamadı. JobId: {JobId}", change.JobId);
        }
    }
}
