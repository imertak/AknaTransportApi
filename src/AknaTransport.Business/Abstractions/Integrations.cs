namespace AknaTransport.Business.Abstractions;

public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType = "application/pdf");

public sealed record EmailMessage(string To, string? ToName, string Subject, string HtmlBody, string TextBody, IReadOnlyList<EmailAttachment> Attachments);

/// <summary>E-posta gönderim sağlayıcısı (SMTP vb.). Başarısızlıkta istisna fırlatır.</summary>
public interface IEmailService
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>Dosya depolama. Yollar her zaman sistem tarafından üretilir, kullanıcıdan alınmaz.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string category, string fileName, byte[] content, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gerçek zamanlı konum yayını için genişleme noktası. Varsayılan uygulama hiçbir şey yapmaz;
/// SignalR hub eklendiğinde bu arayüz uygulanarak canlı yayın yapılabilir.
/// </summary>
public interface ILocationBroadcaster
{
    Task PublishAsync(Features.Locations.LocationDto location, CancellationToken cancellationToken = default);
}
