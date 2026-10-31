using System.Text.Json;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Domain.Interfaces;

namespace AknaTransport.Business.Features.Outbox;

public static class OutboxMessageTypes
{
    public const string InvoiceEmail = "invoice.email";
    public const string PasswordResetEmail = "auth.password-reset-email";
    public const string UserCredentialsSms = "user.credentials-sms";
}

public sealed record InvoiceEmailPayload(Guid InvoiceId, Guid EmailLogId);

public sealed record PasswordResetEmailPayload(Guid UserId, Guid EmailLogId, string Email, string FullName, string Token, DateTime ExpiresAt);

/// <summary>Geçici şifre içerir; mesaj işlendikten sonra içerik silinir.</summary>
public sealed record UserCredentialsSmsPayload(Guid UserId, string PhoneNumber, string FullName, string Email, string TemporaryPassword, bool IsReset);

public static class OutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Outbox kaydını mevcut unit of work'e ekler. Kayıt, iş verisiyle aynı SaveChanges /
    /// transaction içinde kalıcı hale gelir; böylece "fatura kesildi ama mail kuyruğa alınmadı"
    /// durumu oluşmaz.
    /// </summary>
    public static OutboxMessage Enqueue<TPayload>(IRepository<OutboxMessage> outbox, string type, TPayload payload, DateTime now)
    {
        var message = new OutboxMessage
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = OutboxMessageStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        };
        outbox.Add(message);
        return message;
    }

    public static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new InvalidOperationException("Outbox içeriği okunamadı.");
}
