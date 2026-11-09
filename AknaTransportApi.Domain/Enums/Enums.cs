namespace AknaTransportApi.Domain.Enums;

/// <summary>Kullanıcı rolleri. User şoförü ifade eder; Admin aynı zamanda şoför işlemlerini yapabilir.</summary>
public enum UserRole
{
    Admin = 1,
    User = 2,
}

public enum JobStatus
{
    Pending = 0,
    Assigned = 1,
    Accepted = 2,
    OnTheWay = 3,
    Loading = 4,
    Delivering = 5,
    Completed = 6,
    Cancelled = 7,
}

/// <summary>
/// Hazır araç tiplerinin kodları. Araç tipleri artık yönetici tarafından eklenebilir (vehicle_types tablosu);
/// iş ve araç kayıtları tipin kodunu (ör. "Panelvan") metin olarak saklar. Bu sabitler ilk kurulumda gelen tiplerdir.
/// </summary>
public static class VehicleType
{
    public const string Panelvan = "Panelvan";
    public const string SmallPanelvan = "SmallPanelvan";
    public const string Pickup = "Pickup";
    public const string Truck = "Truck";
    public const string Other = "Other";
}

public enum InvoiceStatus
{
    /// <summary>Taslak. Resmi geçerliliği yoktur.</summary>
    Draft = 0,
    /// <summary>e-Arşiv sağlayıcısına gönderildi, sonuç bekleniyor.</summary>
    Pending = 1,
    /// <summary>Gerçek e-Arşiv sağlayıcısı tarafından kesildi.</summary>
    Issued = 2,
    /// <summary>Sağlayıcı faturayı reddetti veya gönderim başarısız oldu.</summary>
    Failed = 3,
    Cancelled = 4,
}

public enum EmailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
}

public enum EmailType
{
    InvoiceDelivery = 0,
    PasswordReset = 1,
    /// <summary>Yöneticinin "E-posta gönder" ile elle gönderdiği fatura e-postası (taslak dahil). Tekrar gönderilebilir.</summary>
    InvoiceManual = 2,
}

public enum OutboxMessageStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2,
}

/// <summary>İş durağının tipi. Bir işte birden fazla yükleme ve teslimat noktası olabilir.</summary>
public enum StopType
{
    Pickup = 0,
    Delivery = 1,
}

/// <summary>
/// İşin faturalandırma durumu. Veritabanında ayrıca tutulmaz; işin bağlı olduğu faturanın
/// (transport_jobs.InvoiceId) durumundan türetilir, böylece iki kayıt arasında tutarsızlık oluşmaz.
/// </summary>
public enum JobBillingStatus
{
    /// <summary>İş hiçbir faturaya bağlı değil.</summary>
    NotInvoiced = 0,
    /// <summary>İş taslak, gönderimde veya hatalı bir faturada; resmi fatura henüz kesilmedi.</summary>
    InDraft = 1,
    /// <summary>e-Arşiv sağlayıcısından başarılı yanıtla kesilmiş faturada.</summary>
    Invoiced = 2,
}

/// <summary>Bildirim türleri. Uygulama türe göre ikon ve yönlendirme seçer.</summary>
public enum NotificationType
{
    /// <summary>İş şoföre atandı (şoföre).</summary>
    JobAssigned = 0,
    /// <summary>İş şoförden alındı veya başka şoföre verildi (eski şoföre).</summary>
    JobUnassigned = 1,
    /// <summary>Atanmış işin bilgileri değişti (şoföre).</summary>
    JobUpdated = 2,
    /// <summary>İş iptal edildi (şoföre).</summary>
    JobCancelled = 3,
    /// <summary>Şoför işi kabul etti (yöneticilere).</summary>
    JobAccepted = 4,
    /// <summary>Şoför yola çıktı (yöneticilere).</summary>
    JobStarted = 5,
    /// <summary>Yükleme başladı (yöneticilere).</summary>
    JobLoading = 6,
    /// <summary>Teslimata çıkıldı (yöneticilere).</summary>
    JobDelivering = 7,
    /// <summary>İş tamamlandı (yöneticilere).</summary>
    JobCompleted = 8,
}

/// <summary>Müşteri cari hesabına elle girilen hareketin türü. Kesilen faturalar ayrıca otomatik borç yazar.</summary>
public enum CustomerTransactionType
{
    /// <summary>Müşteriden gelen ödeme (tahsilat). Bakiyeyi düşürür.</summary>
    Collection = 0,
    /// <summary>Müşteriye giden ödeme (iade, fazla ödemenin geri verilmesi). Bakiyeyi artırır.</summary>
    Refund = 1,
    /// <summary>Faturasız borç kaydı (devir / açılış bakiyesi vb.). Bakiyeyi artırır.</summary>
    Charge = 2,
}
