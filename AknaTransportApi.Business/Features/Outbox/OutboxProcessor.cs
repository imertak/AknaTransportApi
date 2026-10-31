using AknaTransport.Business.Abstractions;
using AknaTransport.Business.Features.Dashboard;
using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Options;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransport.Business.Features.Outbox;

public interface IOutboxProcessor
{
    /// <summary>Zamanı gelmiş outbox mesajlarını işler; işlenen mesaj sayısını döndürür.</summary>
    Task<int> ProcessPendingAsync(CancellationToken ct);
}

/// <summary>
/// Outbox mesajlarını işler. Her mesaj önce koşullu bir UPDATE ile kilitlenir (LockedUntil),
/// böylece birden fazla API örneği aynı mesajı aynı anda işleyemez. Hatalarda üstel geri çekilme
/// ile kontrollü olarak tekrar denenir; MaxAttempts sonrasında Failed olarak işaretlenir.
/// E-posta gönderimi fatura durumunu asla değiştirmez: mail başarısız olsa da fatura yeniden kesilmez.
/// </summary>
public sealed class OutboxProcessor(
    IRepository<OutboxMessage> outbox,
    IRepository<EmailLog> emailLogs,
    IRepository<Invoice> invoices,
    IRepository<User> users,
    IUnitOfWork unitOfWork,
    IEmailService emailService,
    ISmsService smsService,
    IInvoiceService invoiceService,
    IJobReportPdfGenerator reportPdf,
    IBusinessCalendar calendar,
    IOptions<OutboxOptions> options,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    private const string RedactedPayload = "{\"redacted\":true}";

    public async Task<int> ProcessPendingAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var candidates = await outbox.QueryAsNoTracking()
            .Where(m => m.Status == OutboxMessageStatus.Pending && m.NextAttemptAt <= now && (m.LockedUntil == null || m.LockedUntil < now))
            .OrderBy(m => m.CreatedAt).Select(m => m.Id).Take(opts.BatchSize).ToListAsync(ct);

        var processed = 0;
        foreach (var id in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var lockUntil = clock.GetUtcNow().UtcDateTime.AddSeconds(opts.LockSeconds);
            var claimed = await outbox.Query()
                .Where(m => m.Id == id && m.Status == OutboxMessageStatus.Pending && (m.LockedUntil == null || m.LockedUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LockedUntil, lockUntil), ct);
            if (claimed == 0) continue;

            var message = await outbox.Query().FirstAsync(m => m.Id == id, ct);
            try
            {
                await DispatchAsync(message, ct);
                message.Status = OutboxMessageStatus.Processed;
                message.ProcessedAt = clock.GetUtcNow().UtcDateTime;
                message.LockedUntil = null;
                message.LastError = null;
                if (message.Type == OutboxMessageTypes.PasswordResetEmail || message.Type == OutboxMessageTypes.UserCredentialsSms)
                    message.Payload = RedactedPayload;
                await unitOfWork.SaveChangesAsync(ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await HandleFailureAsync(message, ex, ct);
            }
        }
        return processed;
    }

    private Task DispatchAsync(OutboxMessage message, CancellationToken ct) => message.Type switch
    {
        OutboxMessageTypes.InvoiceEmail => SendInvoiceEmailAsync(OutboxWriter.Deserialize<InvoiceEmailPayload>(message.Payload), ct),
        OutboxMessageTypes.PasswordResetEmail => SendPasswordResetAsync(OutboxWriter.Deserialize<PasswordResetEmailPayload>(message.Payload), ct),
        OutboxMessageTypes.UserCredentialsSms => SendCredentialsSmsAsync(OutboxWriter.Deserialize<UserCredentialsSmsPayload>(message.Payload), ct),
        _ => throw new PermanentOutboxException($"Bilinmeyen outbox mesaj tipi: {message.Type}"),
    };

    private async Task SendInvoiceEmailAsync(InvoiceEmailPayload payload, CancellationToken ct)
    {
        var log = await emailLogs.Query().FirstOrDefaultAsync(e => e.Id == payload.EmailLogId, ct)
                  ?? throw new PermanentOutboxException("E-posta kaydı bulunamadı.");

        // Mükerrer gönderim koruması: daha önce gönderildiyse tekrar gönderilmez.
        if (log.Status == EmailStatus.Sent)
        {
            logger.LogInformation("Fatura e-postası zaten gönderilmiş, atlanıyor. InvoiceId: {InvoiceId}", payload.InvoiceId);
            return;
        }

        var invoice = await invoices.QueryAsNoTracking().Where(i => i.Id == payload.InvoiceId)
                          .Select(i => new { i.Status, CustomerName = i.Customer!.CompanyName, i.InvoiceNumber, i.EArchiveInvoiceNumber })
                          .FirstOrDefaultAsync(ct)
                      ?? throw new PermanentOutboxException("Fatura bulunamadı.");
        if (invoice.Status != InvoiceStatus.Issued)
            throw new PermanentOutboxException("Fatura kesilmiş durumda değil; e-posta gönderilmez.");
        if (!emailService.IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış.");

        log.AttemptCount++;
        var officialPdf = await invoiceService.GetOfficialPdfAsync(payload.InvoiceId, ct);
        var report = await invoiceService.BuildJobReportAsync(payload.InvoiceId, ct);
        var reportPdfBytes = reportPdf.Generate(report);
        var number = invoice.EArchiveInvoiceNumber ?? invoice.InvoiceNumber;
        var (html, text) = EmailTemplates.Invoice(invoice.CustomerName);

        await emailService.SendAsync(new EmailMessage(log.RecipientEmail, invoice.CustomerName, log.Subject, html, text,
        [
            new EmailAttachment($"Fatura-{number}.pdf", officialPdf),
            new EmailAttachment($"Nakliye-Is-Dokumu-{number}.pdf", reportPdfBytes),
        ]), ct);

        log.Status = EmailStatus.Sent;
        log.SentAt = clock.GetUtcNow().UtcDateTime;
        log.LastError = null;
        logger.LogInformation("Fatura e-postası gönderildi. InvoiceId: {InvoiceId}, EmailLogId: {EmailLogId}", payload.InvoiceId, log.Id);
    }

    private async Task SendPasswordResetAsync(PasswordResetEmailPayload payload, CancellationToken ct)
    {
        var log = await emailLogs.Query().FirstOrDefaultAsync(e => e.Id == payload.EmailLogId, ct)
                  ?? throw new PermanentOutboxException("E-posta kaydı bulunamadı.");
        if (log.Status == EmailStatus.Sent) return;
        if (payload.ExpiresAt <= clock.GetUtcNow().UtcDateTime) throw new PermanentOutboxException("Sıfırlama kodunun süresi doldu.");
        if (!emailService.IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış.");

        log.AttemptCount++;
        var link = appOptions.Value.PasswordResetUrl?
            .Replace("{token}", Uri.EscapeDataString(payload.Token), StringComparison.Ordinal)
            .Replace("{email}", Uri.EscapeDataString(payload.Email), StringComparison.Ordinal);
        var (html, text) = EmailTemplates.PasswordReset(payload.FullName, payload.Token, link, calendar.ToLocal(payload.ExpiresAt));
        await emailService.SendAsync(new EmailMessage(payload.Email, payload.FullName, log.Subject, html, text, []), ct);
        log.Status = EmailStatus.Sent;
        log.SentAt = clock.GetUtcNow().UtcDateTime;
        log.LastError = null;
    }

    private async Task SendCredentialsSmsAsync(UserCredentialsSmsPayload payload, CancellationToken ct)
    {
        // Şifre bu arada değiştiyse (ör. yeni sıfırlama), eski geçici şifre gönderilmez.
        var stillValid = await users.QueryAsNoTracking().AnyAsync(u => u.Id == payload.UserId && u.IsActive && u.MustChangePassword, ct);
        if (!stillValid) throw new PermanentOutboxException("Kullanıcı pasif veya şifresini zaten değiştirmiş.");
        if (!smsService.IsConfigured) throw new InvalidOperationException("SMS sağlayıcısı yapılandırılmamış.");
        await smsService.SendAsync(new SmsMessage(payload.PhoneNumber,
            EmailTemplates.CredentialsSms(payload.FullName, payload.Email, payload.TemporaryPassword, payload.IsReset)), ct);
        logger.LogInformation("Geçici şifre SMS'i gönderildi. UserId: {UserId}", payload.UserId);
    }

    private async Task HandleFailureAsync(OutboxMessage message, Exception ex, CancellationToken ct)
    {
        var opts = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var permanent = ex is PermanentOutboxException;
        var emailLogId = TryGetEmailLogId(message);
        message.AttemptCount++;
        message.LastError = Truncate(ex.Message);
        message.LockedUntil = null;

        var final = permanent || message.AttemptCount >= opts.MaxAttempts;
        if (final)
        {
            message.Status = OutboxMessageStatus.Failed;
            message.ProcessedAt = now;
            if (message.Type is OutboxMessageTypes.PasswordResetEmail or OutboxMessageTypes.UserCredentialsSms) message.Payload = RedactedPayload;
            logger.LogError(ex, "Outbox mesajı kalıcı olarak başarısız. MessageId: {MessageId}, Type: {Type}", message.Id, message.Type);
        }
        else
        {
            var delay = TimeSpan.FromSeconds(opts.BaseRetryDelaySeconds * Math.Pow(2, message.AttemptCount - 1));
            message.NextAttemptAt = now.Add(delay);
            logger.LogWarning(ex, "Outbox mesajı başarısız, {Delay} sonra tekrar denenecek. MessageId: {MessageId}", delay, message.Id);
        }

        // E-posta kaydına deneme bilgisi yazılır; fatura durumu değişmez.
        if (emailLogId is { } logId)
        {
            var log = await emailLogs.Query().FirstOrDefaultAsync(e => e.Id == logId, ct);
            if (log is not null && log.Status != EmailStatus.Sent)
            {
                log.AttemptCount = Math.Max(log.AttemptCount, message.AttemptCount);
                log.LastError = message.LastError;
                log.Status = final ? EmailStatus.Failed : EmailStatus.Pending;
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static Guid? TryGetEmailLogId(OutboxMessage message)
    {
        try
        {
            return message.Type switch
            {
                OutboxMessageTypes.InvoiceEmail => OutboxWriter.Deserialize<InvoiceEmailPayload>(message.Payload).EmailLogId,
                OutboxMessageTypes.PasswordResetEmail when message.Payload != RedactedPayload => OutboxWriter.Deserialize<PasswordResetEmailPayload>(message.Payload).EmailLogId,
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Truncate(string value) => value.Length > 2000 ? value[..2000] : value;
}

/// <summary>Tekrar denenmesi anlamsız hata (kayıt yok, fatura iptal vb.).</summary>
public sealed class PermanentOutboxException(string message) : Exception(message);
