using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Notifications;

public sealed class NotificationService(
    IRepository<Notification> notifications,
    IRepository<PushDevice> devices,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPushNotificationSender pushSender,
    TimeProvider clock) : INotificationService
{
    public async Task<PagedResult<NotificationDto>> ListMineAsync(NotificationQuery query, CancellationToken ct)
    {
        var q = notifications.QueryAsNoTracking().Where(n => n.UserId == currentUser.UserId);
        if (query.UnreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            // Silinmiş (mantıksal) işe ait bildirim kalır ama işe bağlantı verilmez: Job navigasyonu global filtreyle null döner.
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.Job != null ? n.JobId : null, n.ReadAt != null, n.CreatedAt, n.ReadAt))
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

    public async Task<PushTestResultDto> SendTestAsync(CancellationToken ct)
    {
        var tokens = await devices.QueryAsNoTracking().Where(d => d.UserId == currentUser.UserId).Select(d => d.Token).ToListAsync(ct);
        if (!pushSender.IsConfigured)
            return new PushTestResultDto(false, tokens.Count, 0, ["Sunucuda telefon bildirimleri kapalı (Push__Enabled=false)."]);
        if (tokens.Count == 0)
            return new PushTestResultDto(true, 0, 0, ["Bu hesaba kayıtlı telefon yok. Uygulama bildirim iznini alamamış veya push token'ı oluşturamamış olabilir."]);

        var data = new Dictionary<string, string> { ["type"] = "Test" };
        PushSendResult result;
        try
        {
            result = await pushSender.SendAsync(tokens.Select(t => new PushMessage(t, "Test bildirimi", "Bildirimler bu telefona ulaşıyor.", data, null)).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new PushTestResultDto(true, tokens.Count, 0, [$"Expo'ya ulaşılamadı: {ex.Message}"]);
        }

        if (result.InvalidTokens.Count > 0)
        {
            var invalid = result.InvalidTokens.ToList();
            await devices.Query().Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync(ct);
        }
        var errors = (result.Errors ?? []).Select(e => DescribeError(e.Error, e.Message)).Distinct().ToList();
        var failed = (result.Errors ?? []).Select(e => e.Token).Distinct().Count();
        return new PushTestResultDto(true, tokens.Count, tokens.Count - failed, errors);
    }

    /// <summary>Expo hata kodlarını yöneticinin ne yapacağını söyleyen açıklamaya çevirir.</summary>
    private static string DescribeError(string code, string? message) => code switch
    {
        "InvalidCredentials" => "Expo'da FCM (Android) veya APNs (iPhone) anahtarı tanımlı değil. EAS'e push kimlik bilgilerini yükleyin.",
        "DeviceNotRegistered" => "Telefon kaydı geçersiz (uygulama silinmiş veya yeniden kurulmuş). Uygulamayı açınca yeniden kaydolur.",
        "MessageRateExceeded" => "Bu telefona çok sık bildirim gönderildi, biraz sonra tekrar deneyin.",
        _ => string.IsNullOrWhiteSpace(message) ? code : $"{code}: {message}",
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
