using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Services.Locations;

public sealed class LocationService(
    IRepository<TransportJob> jobs,
    IRepository<Location> locations,
    IRepository<JobLatestLocation> latestLocations,
    IRepository<JobTrackingToken> trackingTokens,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITokenService tokenService,
    ILocationBroadcaster broadcaster,
    IOptions<LocationTrackingOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<LocationService> logger) : ILocationService
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxBacklog = TimeSpan.FromHours(12);

    public async Task<LocationUpdateResult> UpdateAsync(UpdateLocationRequest request, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var job = await jobs.QueryAsNoTracking().Where(j => j.Id == request.JobId)
                      .Select(j => new { j.Id, j.Status, j.AssignedDriverId }).FirstOrDefaultAsync(ct)
                  ?? throw NotFoundException.For("İş");

        // Konumu yalnızca işe atanmış kişinin cihazı gönderebilir (admin dahil): başka şoförün konumu değiştirilemez.
        if (job.AssignedDriverId != currentUser.UserId) throw new ForbiddenException("Bu iş için konum gönderme yetkiniz yok.");
        if (!TransportJob.TrackableStatuses.Contains(job.Status))
            throw new ConflictException("Konum yalnızca aktif (yolda, yüklemede veya teslimatta) işler için kabul edilir.");

        var incoming = request.AllPoints();
        var points = incoming
            .Select(p => new { Point = p, At = Normalize(p.RecordedAt ?? now) })
            .Where(p => p.At <= now + MaxClockSkew && p.At >= now - MaxBacklog)
            .Where(p => p.Point.Accuracy is null || p.Point.Accuracy <= opts.MaxAccuracyMeters)
            .OrderBy(p => p.At)
            .TakeLast(opts.MaxBatchSize)
            .ToList();

        var latest = await latestLocations.Query().FirstOrDefaultAsync(l => l.JobId == job.Id, ct);
        var lastLat = latest?.Latitude;
        var lastLng = latest?.Longitude;
        var lastAt = latest?.RecordedAt;
        var stored = new List<Location>();

        foreach (var item in points)
        {
            var p = item.Point;
            if (lastAt is { } prevAt)
            {
                if (item.At <= prevAt) continue; // eski veya tekrar gönderilmiş konum
                var elapsed = (item.At - prevAt).TotalSeconds;
                var distance = Geo.DistanceMeters(lastLat!.Value, lastLng!.Value, p.Latitude, p.Longitude);
                var significant = distance >= opts.MinDistanceMeters && elapsed >= opts.MinIntervalSeconds;
                var heartbeat = elapsed >= opts.HeartbeatSeconds;
                if (!significant && !heartbeat) continue;
            }

            stored.Add(new Location
            {
                JobId = job.Id,
                DriverId = currentUser.UserId,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Accuracy = p.Accuracy,
                Speed = p.Speed,
                Heading = p.Heading,
                RecordedAt = item.At,
            });
            lastLat = p.Latitude;
            lastLng = p.Longitude;
            lastAt = item.At;
        }

        LocationDto? latestDto = latest is null ? null : ToDto(latest);
        if (stored.Count > 0)
        {
            var last = stored[^1];
            if (latest is null)
            {
                latest = new JobLatestLocation { JobId = job.Id };
                latestLocations.Add(latest);
            }
            latest.DriverId = last.DriverId;
            latest.Latitude = last.Latitude;
            latest.Longitude = last.Longitude;
            latest.Accuracy = last.Accuracy;
            latest.Speed = last.Speed;
            latest.Heading = last.Heading;
            latest.RecordedAt = last.RecordedAt;
            latest.UpdatedAt = now;

            locations.AddRange(stored);
            await unitOfWork.SaveChangesAsync(ct);
            latestDto = ToDto(latest);

            try
            {
                await broadcaster.PublishAsync(latestDto, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Konum yayını başarısız. JobId: {JobId}", job.Id);
            }
        }

        return new LocationUpdateResult(incoming.Count, stored.Count, incoming.Count - stored.Count, latestDto);
    }

    public async Task<LocationDto> GetLatestAsync(Guid jobId, CancellationToken ct)
    {
        await EnsureCanViewAsync(jobId, ct);
        return await latestLocations.QueryAsNoTracking().Where(l => l.JobId == jobId)
                   .Select(l => new LocationDto(l.JobId, l.DriverId, l.Latitude, l.Longitude, l.Accuracy, l.Speed, l.Heading, l.RecordedAt))
                   .FirstOrDefaultAsync(ct)
               ?? throw new NotFoundException("Bu iş için henüz konum bilgisi yok.");
    }

    public async Task<PagedResult<LocationDto>> GetHistoryAsync(Guid jobId, LocationHistoryQuery query, CancellationToken ct)
    {
        await EnsureCanViewAsync(jobId, ct);
        if (query.PageSize > 500) query.PageSize = 500;
        var q = locations.QueryAsNoTracking().Where(l => l.JobId == jobId);
        if (query.From is { } from) q = q.Where(l => l.RecordedAt >= Normalize(from));
        if (query.To is { } to) q = q.Where(l => l.RecordedAt <= Normalize(to));
        q = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? q.OrderByDescending(l => l.RecordedAt)
            : q.OrderBy(l => l.RecordedAt);
        return await q.Select(l => new LocationDto(l.JobId, l.DriverId, l.Latitude, l.Longitude, l.Accuracy, l.Speed, l.Heading, l.RecordedAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<IReadOnlyList<ActiveJobLocationDto>> GetActiveJobsAsync(CancellationToken ct)
    {
        var trackable = TransportJob.TrackableStatuses.ToList();
        var q = jobs.QueryAsNoTracking().Where(j => trackable.Contains(j.Status));
        if (!currentUser.IsAdmin) q = q.Where(j => j.AssignedDriverId == currentUser.UserId);

        // Son konum tablosu ile tek sorguda left join; geçmiş tablosuna gidilmez.
        var rows = await (from j in q
                          join l in latestLocations.QueryAsNoTracking() on j.Id equals l.JobId into lj
                          from l in lj.DefaultIfEmpty()
                          orderby j.JobDate, j.JobNumber
                          select new
                          {
                              j.Id, j.JobNumber, j.Status, CustomerName = j.Customer!.CompanyName, j.StoreName, j.VehicleType,
                              j.PickupAddress, j.DeliveryAddress, j.AssignedDriverId,
                              DriverName = j.AssignedDriver != null ? j.AssignedDriver.FullName : null,
                              DriverPhone = j.AssignedDriver != null ? j.AssignedDriver.PhoneNumber : null,
                              Location = l,
                          }).ToListAsync(ct);

        return rows.Select(r => new ActiveJobLocationDto(r.Id, r.JobNumber, r.Status, r.CustomerName, r.StoreName, r.VehicleType,
            r.PickupAddress, r.DeliveryAddress, r.AssignedDriverId, r.DriverName, r.DriverPhone,
            r.Location is null ? null : ToDto(r.Location))).ToList();
    }

    public async Task<TrackingTokenDto> CreateTrackingTokenAsync(Guid jobId, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
        var exists = await jobs.QueryAsNoTracking().AnyAsync(j => j.Id == jobId && j.Status != JobStatus.Cancelled, ct);
        if (!exists) throw NotFoundException.For("İş");

        var now = clock.GetUtcNow().UtcDateTime;
        var raw = tokenService.GenerateSecureToken();
        var entity = new JobTrackingToken
        {
            JobId = jobId,
            TokenHash = tokenService.HashToken(raw),
            CreatedAt = now,
            CreatedByUserId = currentUser.UserId,
            ExpiresAt = now.AddDays(appOptions.Value.TrackingTokenDays),
        };
        trackingTokens.Add(entity);
        await unitOfWork.SaveChangesAsync(ct);
        var url = appOptions.Value.TrackingUrl?.Replace("{token}", Uri.EscapeDataString(raw), StringComparison.Ordinal);
        return new TrackingTokenDto(raw, url, entity.ExpiresAt);
    }

    public async Task RevokeTrackingTokensAsync(Guid jobId, CancellationToken ct)
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
        var now = clock.GetUtcNow().UtcDateTime;
        var tokens = await trackingTokens.Query().Where(t => t.JobId == jobId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PublicTrackingDto> GetPublicTrackingAsync(string token, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = tokenService.HashToken(token);
        var trackable = TransportJob.TrackableStatuses.ToList();
        var row = await (from t in trackingTokens.QueryAsNoTracking()
                         where t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > now
                         join j in jobs.QueryAsNoTracking() on t.JobId equals j.Id
                         join l in latestLocations.QueryAsNoTracking() on j.Id equals l.JobId into lj
                         from l in lj.DefaultIfEmpty()
                         select new { j.JobNumber, j.Status, j.StoreName, j.JobDate, j.DeliveryAddress, Location = l })
                        .FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Takip bağlantısı geçersiz veya süresi dolmuş.");

        // Konum yalnızca araç hareket halindeyken paylaşılır.
        var location = row.Location is not null && trackable.Contains(row.Status) ? ToDto(row.Location) : null;
        return new PublicTrackingDto(row.JobNumber, row.Status, row.StoreName, row.JobDate, row.DeliveryAddress, location);
    }

    public async Task<int> PurgeExpiredHistoryAsync(CancellationToken ct)
    {
        var days = options.Value.RetentionDays;
        if (days <= 0) return 0;
        var threshold = clock.GetUtcNow().UtcDateTime.AddDays(-days);
        return await locations.Query().Where(l => l.RecordedAt < threshold).ExecuteDeleteAsync(ct);
    }

    private async Task EnsureCanViewAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.QueryAsNoTracking().Where(j => j.Id == jobId).Select(j => new { j.AssignedDriverId }).FirstOrDefaultAsync(ct)
                  ?? throw NotFoundException.For("İş");
        if (!currentUser.IsAdmin && job.AssignedDriverId != currentUser.UserId) throw new ForbiddenException("Bu işin konumunu görüntüleme yetkiniz yok.");
    }

    private static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static LocationDto ToDto(JobLatestLocation l) =>
        new(l.JobId, l.DriverId, l.Latitude, l.Longitude, l.Accuracy, l.Speed, l.Heading, l.RecordedAt);
}
