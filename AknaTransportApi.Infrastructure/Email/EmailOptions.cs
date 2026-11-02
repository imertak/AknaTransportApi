using AknaTransportApi.Business.Dtos.Integrations;

namespace AknaTransportApi.Infrastructure.Email;

/// <summary>
/// E-posta ayarları. Provider=Resend ise Resend HTTP API'si (Email__ResendApiKey), aksi halde SMTP kullanılır.
/// Gizli değerler (API anahtarı, SMTP şifresi) yalnızca environment variable ile verilir.
/// Development için PickupDirectory tanımlanırsa (SMTP modunda) e-postalar gönderilmek yerine .eml dosyası olarak yazılır.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" (varsayılan) veya "Resend".</summary>
    public string Provider { get; set; } = "Smtp";
    /// <summary>Resend API anahtarı (re_...). Koda/appsettings'e yazılmaz: Email__ResendApiKey.</summary>
    public string? ResendApiKey { get; set; }
    public string ResendApiUrl { get; set; } = "https://api.resend.com";

    public bool UseResend => string.Equals(Provider, "Resend", StringComparison.OrdinalIgnoreCase);

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromEmail { get; set; }
    public string FromName { get; set; } = "AKNA Transport";
    /// <summary>Auto (önerilen), SslOnConnect, StartTls, None.</summary>
    public string Security { get; set; } = "Auto";
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Her gönderilen e-postaya CC olarak eklenen adresler (';' veya ',' ile ayrılmış). Alıcıda zaten varsa tekrar eklenmez.</summary>
    public string? AlwaysCc { get; set; }

    /// <summary>Mesajın CC listesine <see cref="AlwaysCc"/> adreslerini ekler; alıcı ve tekrar eden adresler çıkarılır.</summary>
    public IReadOnlyList<string> CcFor(EmailMessage message)
    {
        var extra = (AlwaysCc ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { message.To.Trim() };
        return message.Cc.Concat(extra).Select(a => a.Trim()).Where(a => a.Length > 0 && seen.Add(a)).ToList();
    }

    /// <summary>Yalnızca geliştirme: e-postaları bu klasöre .eml olarak yazar.</summary>
    public string? PickupDirectory { get; set; }
}
