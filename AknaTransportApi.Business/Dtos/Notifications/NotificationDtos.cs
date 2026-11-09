namespace AknaTransportApi.Business.Dtos.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    Guid? JobId,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt);

public sealed class NotificationQuery : PagedQuery
{
    /// <summary>true ise yalnızca okunmamış bildirimler.</summary>
    public bool UnreadOnly { get; set; }
}

public sealed record UnreadCountDto(int Count);

/// <summary>Uygulamanın aldığı Expo push token'ı (ExponentPushToken[...]) ve platformu (ios/android).</summary>
public sealed record RegisterPushDeviceRequest(string Token, string? Platform);

public sealed record UnregisterPushDeviceRequest(string Token);

/// <summary>Push sağlayıcısına gönderilecek tek mesaj.</summary>
public sealed record PushMessage(string Token, string Title, string Body, IReadOnlyDictionary<string, string> Data, int? Badge);

/// <summary>
/// Gönderim sonucu: artık geçerli olmayan (uygulama silinmiş vb.) token'lar silinir.
/// Errors, sağlayıcının reddettiği her mesajı içerir (ör. InvalidCredentials: FCM/APNs anahtarı Expo'ya yüklenmemiş).
/// </summary>
public sealed record PushSendResult(IReadOnlyList<string> InvalidTokens, IReadOnlyList<PushTicketError>? Errors = null);

/// <summary>Expo'nun tek bir mesaj için döndürdüğü hata (details.error ve message).</summary>
public sealed record PushTicketError(string Token, string Error, string? Message);

/// <summary>Test bildiriminin sonucu: sunucuda push açık mı, kaç cihaz kayıtlı, kaçına gitti ve hatalar.</summary>
public sealed record PushTestResultDto(bool Enabled, int DeviceCount, int Delivered, IReadOnlyList<string> Errors);
