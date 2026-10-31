namespace AknaTransport.Infrastructure.Email;

/// <summary>
/// SMTP ayarları. Şifre environment variable ile verilir: Email__Password.
/// Development için PickupDirectory tanımlanırsa e-postalar gönderilmek yerine .eml dosyası olarak yazılır.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromEmail { get; set; }
    public string FromName { get; set; } = "AKNA Transport";
    /// <summary>Auto (önerilen), SslOnConnect, StartTls, None.</summary>
    public string Security { get; set; } = "Auto";
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Yalnızca geliştirme: e-postaları bu klasöre .eml olarak yazar.</summary>
    public string? PickupDirectory { get; set; }
}
