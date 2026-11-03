using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Notifications;

public sealed class NotificationService(
    IRepository<Notification> notifications,
    IRepository<PushDevice> devices,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> ListMineAsync(NotificationQuery query, CancellationToken ct)
    {
        var q = notifications.QueryAsNoTracking().Where(n => n.UserId == currentUser.UserId);
        if (query.UnreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.JobId, n.ReadAt != null, n.CreatedAt, n.ReadAt))
            .ToPagedResultAsync(query, ct);
    }

    public async Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct) =>
        new(await notifications.QueryAsNoTracking().CountAsync(n => n.UserId == currentUser.UserId && n.ReadAt == null, ct));

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var item = await notifications.Query().FirstOrDefaultAsync(n => n.Id == id && n.UserId == currentUser.UserId, ct)
                   ?? throw NotFoundException.For("Bildirim");
        if (item.ReadAt is not null) return;
        item.ReadAt = Now;
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var now = Now;
        return await notifications.Query().Where(n => n.UserId == currentUser.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    public async Task RegisterDeviceAsync(RegisterPushDeviceRequest request, CancellationToken ct)
    {
        var token = request.Token.Trim();
        var platform = request.Platform?.Trim().ToLowerInvariant();
        var now = Now;
        var device = await devices.Query().FirstOrDefaultAsync(d => d.Token == token, ct);
        if (device is null)
        {
            devices.Add(new PushDevice { UserId = currentUser.UserId, Token = token, Platform = platform, CreatedAt = now, LastSeenAt = now });
        }
        else
        {
            // Aynı telefonda başka kullanıcı giriş yaptıysa bildirimler artık yeni kullanıcıya gider.
            device.UserId = currentUser.UserId;
            device.Platform = platform ?? device.Platform;
            device.LastSeenAt = now;
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task UnregisterDeviceAsync(UnregisterPushDeviceRequest request, CancellationToken ct)
    {
        var token = request.Token.Trim();
        await devices.Query().Where(d => d.Token == token && d.UserId == currentUser.UserId).ExecuteDeleteAsync(ct);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
