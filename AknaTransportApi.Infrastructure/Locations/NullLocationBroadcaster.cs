using AknaTransport.Business.Abstractions;
using AknaTransport.Business.Features.Locations;

namespace AknaTransport.Infrastructure.Locations;

/// <summary>
/// Varsayılan konum yayıncısı: gerçek zamanlı yayın yapmaz. SignalR eklendiğinde
/// <see cref="ILocationBroadcaster"/> bir hub üzerinden "job-{id}" ve "admins" gruplarına
/// yayın yapan bir uygulamayla değiştirilir; servis kodu değişmez.
/// </summary>
public sealed class NullLocationBroadcaster : ILocationBroadcaster
{
    public Task PublishAsync(LocationDto location, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
