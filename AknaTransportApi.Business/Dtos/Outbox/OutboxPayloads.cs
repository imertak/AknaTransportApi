using System.Text.Json;

namespace AknaTransportApi.Business.Dtos.Outbox;

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
