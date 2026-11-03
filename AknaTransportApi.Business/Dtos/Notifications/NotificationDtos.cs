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

/// <summary>Gönderim sonucu: artık geçerli olmayan (uygulama silinmiş vb.) token'lar silinir.</summary>
public sealed record PushSendResult(IReadOnlyList<string> InvalidTokens);
