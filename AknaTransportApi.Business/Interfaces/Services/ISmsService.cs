namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>
/// SMS sağlayıcısı soyutlaması (Netgsm, İleti Merkezi, Twilio vb.). Somut sağlayıcılar
/// Infrastructure katmanında adapter olarak eklenir. Yapılandırılmamışsa <see cref="IsConfigured"/> false döner.
/// </summary>
public interface ISmsService
{
    bool IsConfigured { get; }
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
