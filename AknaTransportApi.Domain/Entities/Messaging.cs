using AknaTransport.Domain.Common;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Domain.Entities;

/// <summary>
/// E-posta gönderim kaydı. Fatura e-postası için fatura başına tek kayıt bulunur
/// (InvoiceId benzersiz indeks) ve bu, mükerrer gönderimi veritabanı seviyesinde engeller.
/// </summary>
public class EmailLog : Entity
{
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public EmailType Type { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public EmailStatus Status { get; set; } = EmailStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Transactional outbox kaydı. İş verisiyle aynı transaction içinde yazılır,
/// arka plan işleyicisi tarafından en az bir kez işlenir.
/// </summary>
public class OutboxMessage : Entity
{
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    /// <summary>İşleme kilidinin bitiş zamanı; birden fazla API örneğinin aynı mesajı almasını engeller.</summary>
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
