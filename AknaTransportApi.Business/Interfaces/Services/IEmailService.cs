namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>E-posta gönderim sağlayıcısı (SMTP vb.). Başarısızlıkta istisna fırlatır.</summary>
public interface IEmailService
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
