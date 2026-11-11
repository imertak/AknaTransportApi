using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Services.Outbox;

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
    IRepository<Notification> notifications,
    IRepository<PushDevice> pushDevices,
    IUnitOfWork unitOfWork,
    IPushNotificationSender pushSender,
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
        OutboxMessageTypes.PushNotification => SendPushAsync(OutboxWriter.Deserialize<PushNotificationPayload>(message.Payload), ct),
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
                          .Select(i => new { i.Status, i.InvoiceNumber, i.EArchiveInvoiceNumber, i.EArchiveProvider })
                          .FirstOrDefaultAsync(ct)
                      ?? throw new PermanentOutboxException("Fatura bulunamadı.");
        var manual = log.Type == EmailType.InvoiceManual;
        // Otomatik e-posta yalnızca kesilmiş faturada gider; elle gönderimde taslak da gönderilebilir (iptal edilmiş hariç).
        if (!manual && invoice.Status != InvoiceStatus.Issued)
            throw new PermanentOutboxException("Fatura kesilmiş durumda değil; e-posta gönderilmez.");
        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new PermanentOutboxException("Fatura iptal edilmiş; e-posta gönderilmez.");
        if (!emailService.IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış.");

        log.AttemptCount++;
        var issued = invoice.Status == InvoiceStatus.Issued;
        var number = invoice.EArchiveInvoiceNumber ?? invoice.InvoiceNumber;
        // Kesilmiş fatura: GİB portalındaki imzalı belgenin PDF'i (e-Arsiv_Fatura_<no>.pdf). Sunucuda tarayıcı yoksa
        // GİB no ve ETTN ile üretilen PDF gönderilir ve GİB'in orijinal belgesi HTML olarak eklenir.
        // Taslak: "resmi fatura değildir" ibareli önizleme PDF'i; fatura durumu değişmez.
        EmailAttachment invoicePdf;
        FileDownload? original = null;
        if (issued)
        {
            var official = await invoiceService.GetOfficialDocumentAsync(payload.InvoiceId, ct);
            // Elle yüklenen fatura başka yerde kesildi; e-Arşiv adı verilmez.
            invoicePdf = invoice.EArchiveProvider == Invoice.ManualProvider
                ? new EmailAttachment($"Fatura_{number}.pdf", official.Content)
                : official.FromProvider
                ? new EmailAttachment($"e-Arsiv_Fatura_{number}.pdf", official.Content)
                : new EmailAttachment($"Fatura-{number}.pdf", official.Content);
            if (!official.FromProvider) original = await invoiceService.GetIssuedEArchiveDocumentAsync(payload.InvoiceId, ct);
        }
        else
        {
            invoicePdf = new EmailAttachment($"Fatura_Taslagi_{invoice.InvoiceNumber}.pdf", (await invoiceService.GetPdfAsync(payload.InvoiceId, ct)).Content);
        }
        // Serbest (işe bağlı olmayan) faturada iş dökümü yoktur; yalnızca fatura gönderilir.
        var report = await invoiceService.BuildJobReportAsync(payload.InvoiceId, ct);
        EmailAttachment? reportAttachment = report.Rows.Count > 0
            ? new EmailAttachment($"Is_Dokumu_{invoice.InvoiceNumber}.pdf", reportPdf.Generate(report))
            : null;
        var detail = await invoiceService.GetAsync(payload.InvoiceId, ct);
        var jobCount = detail.Items.Count(i => i.JobId is not null);
        var attachmentNames = new List<string>();
        if (original is not null) attachmentNames.Add(original.FileName);
        attachmentNames.Add(invoicePdf.FileName);
        if (reportAttachment is not null) attachmentNames.Add(reportAttachment.FileName);
        var (html, text) = EmailTemplates.Invoice(new InvoiceEmailModel(
            detail.CustomerName, number, detail.InvoiceDate, jobCount, detail.SubTotal, detail.VatAmount,
            detail.WithholdingCode is null ? detail.GrandTotal : detail.PayableAmount, detail.Currency, detail.Iban, detail.BankName,
            Draft: !issued, Attachments: attachmentNames));

        // Kayıtta alıcılar "; " ile ayrılır: ilki ana alıcı, diğerleri bilgi (CC).
        var recipients = log.RecipientEmail.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await emailService.SendAsync(new EmailMessage(recipients[0], detail.CustomerName, log.Subject, html, text,
            new[]
            {
                EmailTemplates.Logo,
                original is null ? null : new EmailAttachment(original.FileName, original.Content, original.ContentType),
                invoicePdf,
                reportAttachment,
            }.OfType<EmailAttachment>().ToList(),
            IdempotencyKey: $"invoice-email-{log.Id:N}") { Cc = recipients.Skip(1).ToList() }, ct);

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
        await emailService.SendAsync(new EmailMessage(payload.Email, payload.FullName, log.Subject, html, text, [EmailTemplates.Logo]), ct);
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

    /// <summary>
    /// Bildirimi kullanıcının kayıtlı tüm cihazlarına gönderir. Cihaz yoksa veya push kapalıysa sessizce geçilir;
    /// bildirim uygulama içi listede zaten görünür. Geçersizleşen token'lar silinir.
    /// </summary>
    private async Task SendPushAsync(PushNotificationPayload payload, CancellationToken ct)
    {
        if (!pushSender.IsConfigured) return;
        var n = await notifications.QueryAsNoTracking().Where(x => x.Id == payload.NotificationId)
            .Select(x => new { x.UserId, x.Type, x.Title, x.Body, x.JobId, x.ReadAt, Active = x.User!.IsActive })
            .FirstOrDefaultAsync(ct);
        // Silinmiş, okunmuş veya pasif kullanıcıya ait bildirim telefona gönderilmez.
        if (n is null || n.ReadAt is not null || !n.Active) return;
        var tokens = await pushDevices.QueryAsNoTracking().Where(d => d.UserId == n.UserId).Select(d => d.Token).ToListAsync(ct);
        if (tokens.Count == 0) return;

        var unread = await notifications.QueryAsNoTracking().CountAsync(x => x.UserId == n.UserId && x.ReadAt == null, ct);
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = payload.NotificationId.ToString(),
            ["type"] = n.Type.ToString(),
        };
        if (n.JobId is { } jobId) data["jobId"] = jobId.ToString();

        var result = await pushSender.SendAsync(tokens.Select(t => new PushMessage(t, n.Title, n.Body, data, unread)).ToList(), ct);
        if (result.InvalidTokens.Count > 0)
        {
            var invalid = result.InvalidTokens.ToList();
            await pushDevices.Query().Where(d => invalid.Contains(d.Token)).ExecuteDeleteAsync(ct);
            logger.LogInformation("{Count} geçersiz push token silindi.", invalid.Count);
        }
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
