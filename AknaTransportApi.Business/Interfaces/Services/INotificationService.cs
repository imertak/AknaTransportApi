namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Giriş yapmış kullanıcının bildirimleri ve push cihazları.</summary>
public interface INotificationService
{
    Task<PagedResult<NotificationDto>> ListMineAsync(NotificationQuery query, CancellationToken ct);
    Task<UnreadCountDto> GetUnreadCountAsync(CancellationToken ct);
    Task MarkReadAsync(Guid id, CancellationToken ct);
    Task<int> MarkAllReadAsync(CancellationToken ct);
    Task RegisterDeviceAsync(RegisterPushDeviceRequest request, CancellationToken ct);
    Task UnregisterDeviceAsync(UnregisterPushDeviceRequest request, CancellationToken ct);
    /// <summary>Kullanıcının kayıtlı tüm cihazlarına hemen bir deneme bildirimi gönderir ve sonucu döner.</summary>
    Task<PushTestResultDto> SendTestAsync(CancellationToken ct);
}

/// <summary>Telefon bildirimi sağlayıcısı (Expo Push). Infrastructure katmanında uygulanır.</summary>
public interface IPushNotificationSender
{
    bool IsConfigured { get; }
    Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct);
}
