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

public enum VehicleType
{
    Panelvan = 0,
    SmallPanelvan = 1,
    Pickup = 2,
    Truck = 3,
    Other = 4,
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
