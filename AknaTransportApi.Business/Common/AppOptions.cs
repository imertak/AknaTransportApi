namespace AknaTransportApi.Business.Common;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Şifre sıfırlama e-postasındaki bağlantı. {token} ve {email} yer tutucuları desteklenir.</summary>
    public string? PasswordResetUrl { get; set; }

    public int PasswordResetTokenMinutes { get; set; } = 30;

    /// <summary>Müşteri takip bağlantısı tabanı. {token} yer tutucusu desteklenir.</summary>
    public string? TrackingUrl { get; set; }

    public int TrackingTokenDays { get; set; } = 7;

    public string CompanyName { get; set; } = "AKNA Transport";

    public decimal DefaultVatRate { get; set; } = 20;

    /// <summary>Faturalarda yazılacak firma IBAN'ı (environment variable: App__CompanyIban).</summary>
    public string? CompanyIban { get; set; }

    /// <summary>IBAN'ın ait olduğu banka adı (ör. "Ziraat Bankası"). Faturada IBAN ile birlikte yazılır.</summary>
    public string? CompanyBankName { get; set; }

    /// <summary>Varsayılan KDV tevkifat kodu (ör. 624 Yük taşımacılığı). Boşsa faturalar tevkifatsız açılır.</summary>
    public string? DefaultWithholdingCode { get; set; }

    /// <summary>Raporlarda ve dashboard'da "bugün" hesabı için saat dilimi.</summary>
    public string TimeZoneId { get; set; } = "Europe/Istanbul";
}

public sealed class LocationTrackingOptions
{
    public const string SectionName = "LocationTracking";

    /// <summary>Bu mesafenin altındaki hareketler geçmişe yazılmaz (metre).</summary>
    public double MinDistanceMeters { get; set; } = 30;

    /// <summary>İki geçmiş kaydı arasındaki minimum süre (saniye).</summary>
    public int MinIntervalSeconds { get; set; } = 15;

    /// <summary>Araç dursa bile bu sürede bir kayıt alınır (saniye).</summary>
    public int HeartbeatSeconds { get; set; } = 120;

    /// <summary>Tek istekte kabul edilen maksimum konum sayısı (çevrimdışı toplu gönderim).</summary>
    public int MaxBatchSize { get; set; } = 50;

    /// <summary>Bu değerden kötü doğruluktaki (metre) konumlar reddedilir.</summary>
    public double MaxAccuracyMeters { get; set; } = 200;

    /// <summary>Konum geçmişinin saklanma süresi (gün). 0 ise silinmez.</summary>
    public int RetentionDays { get; set; } = 90;
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int MaxAttempts { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 10;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int LockSeconds { get; set; } = 120;
}
