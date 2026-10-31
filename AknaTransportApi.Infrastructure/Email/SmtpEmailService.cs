using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AknaTransportApi.Infrastructure.Email;

/// <summary>MailKit tabanlı SMTP gönderici. Yapılandırma eksikse gönderim yapmaz ve hata fırlatır.</summary>
public sealed class SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger) : IEmailService
{
    private readonly EmailOptions _options = options.Value;

    private bool UsePickup => !string.IsNullOrWhiteSpace(_options.PickupDirectory);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.FromEmail) && (UsePickup || !string.IsNullOrWhiteSpace(_options.Host));

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış (Email:Host / Email:FromEmail).");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail!));
        mime.To.Add(new MailboxAddress(message.ToName ?? message.To, message.To));
        mime.Subject = message.Subject;

        var body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody };
        foreach (var attachment in message.Attachments)
            body.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        mime.Body = body.ToMessageBody();

        if (UsePickup)
        {
            Directory.CreateDirectory(_options.PickupDirectory!);
            var path = Path.Combine(_options.PickupDirectory!, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
            await mime.WriteToAsync(path, cancellationToken);
            logger.LogInformation("E-posta pickup klasörüne yazıldı: {Path}", path);
            return;
        }

        using var client = new SmtpClient { Timeout = _options.TimeoutSeconds * 1000 };
        var security = _options.Security.ToLowerInvariant() switch
        {
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto,
        };
        await client.ConnectAsync(_options.Host!, _options.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(_options.Username))
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        logger.LogInformation("E-posta gönderildi. Konu: {Subject}", message.Subject);
    }
}
