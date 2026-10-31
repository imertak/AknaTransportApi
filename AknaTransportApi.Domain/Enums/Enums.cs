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
