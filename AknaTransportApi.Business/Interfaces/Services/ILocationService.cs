using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface ILocationService
{
    Task<LocationUpdateResult> UpdateAsync(UpdateLocationRequest request, CancellationToken ct);

    /// <summary>Şoförün tüm aktif (takip edilen) işlerine aynı konum akışını yazar.</summary>
    Task<DriverLocationUpdateResult> UpdateMyLocationAsync(DriverLocationRequest request, CancellationToken ct);
    Task<LocationDto> GetLatestAsync(Guid jobId, CancellationToken ct);
    Task<PagedResult<LocationDto>> GetHistoryAsync(Guid jobId, LocationHistoryQuery query, CancellationToken ct);
    Task<IReadOnlyList<ActiveJobLocationDto>> GetActiveJobsAsync(CancellationToken ct);
    Task<TrackingTokenDto> CreateTrackingTokenAsync(Guid jobId, CancellationToken ct);
    Task RevokeTrackingTokensAsync(Guid jobId, CancellationToken ct);
    Task<PublicTrackingDto> GetPublicTrackingAsync(string token, CancellationToken ct);
    Task<int> PurgeExpiredHistoryAsync(CancellationToken ct);
}
