namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Canlı takibin neden durdurulduğu.</summary>
public enum JobTrackingStopReason
{
    Cancelled = 0,
    Deleted = 1,
}

/// <summary>
/// İş iptal edildiğinde veya silindiğinde, iş kaydı kaydedildikten sonra çağrılan kanca.
/// Birden fazla uygulama kaydedilebilir (ör. müşteri takip bağlantılarını kapatma, SignalR ile
/// "takip durdu" yayını). Hata fırlatan bir uygulama iptal/silme işlemini geri almaz; hata loglanır.
/// </summary>
public interface IJobTrackingStopHandler
{
    Task OnTrackingStoppedAsync(Guid jobId, JobTrackingStopReason reason, CancellationToken ct);
}
