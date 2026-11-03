namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Kullanıcıya gösterilen uygulama içi bildirim (ör. "Yeni iş atandı"). İş verisiyle aynı transaction'da
/// yazılır; telefon bildirimi (push) outbox üzerinden ayrıca gönderilir. Push gitmese de kayıt listede görünür.
/// </summary>
public class Notification : Entity
{
    public const int TitleMaxLength = 150;
    public const int BodyMaxLength = 500;

    public Guid UserId { get; set; }
    public User? User { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    /// <summary>Bildirimin ilgili olduğu iş. İş silinirse boşaltılır.</summary>
    public Guid? JobId { get; set; }
    public TransportJob? Job { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsRead => ReadAt is not null;
}

/// <summary>
/// Kullanıcının push bildirimi alacağı cihaz (Expo push token). Aynı token tek kullanıcıya aittir;
/// telefonda başka kullanıcı giriş yaparsa token yeni kullanıcıya taşınır.
/// </summary>
public class PushDevice : Entity
{
    public const int TokenMaxLength = 200;

    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Token { get; set; } = string.Empty;
    /// <summary>ios / android.</summary>
    public string? Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
