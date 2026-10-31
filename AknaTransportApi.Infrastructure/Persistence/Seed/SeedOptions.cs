namespace AknaTransportApi.Infrastructure.Persistence.Seed;

/// <summary>
/// Seed ayarları. Şifreler kaynak kodda tutulmaz; environment variable veya User Secrets ile verilir:
/// Seed__AdminEmail, Seed__AdminPassword, Seed__DemoUserPassword.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>İlk yönetici hesabı. Sistemde aktif yönetici yoksa oluşturulur (tüm ortamlarda).</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "AKNA Yönetici";
    public string AdminPhoneNumber { get; set; } = "+905000000000";

    /// <summary>Örnek veri yalnızca Development ortamında ve bu ayar true iken oluşturulur.</summary>
    public bool DemoData { get; set; }

    /// <summary>Örnek şoför hesaplarının şifresi. Verilmezse örnek şoförler oluşturulmaz.</summary>
    public string? DemoUserPassword { get; set; }
}

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Uygulama açılışında bekleyen migration'ları uygular.</summary>
    public bool ApplyMigrationsOnStartup { get; set; }
}
