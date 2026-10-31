using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.Sms;

/// <summary>
/// SMS ayarları. Hassas alanlar environment variable ile verilir: Sms__Username, Sms__Password, Sms__ApiKey.
/// </summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Kullanılacak sağlayıcı adaptörünün adı (ör. "Netgsm"). Boşsa SMS gönderilmez.</summary>
    public string? Provider { get; set; }
    public string? ApiUrl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Onaylı SMS başlığı (originator).</summary>
    public string? Sender { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Somut SMS sağlayıcısı. Yeni bir sağlayıcı eklemek için bu arayüzü uygulayan bir sınıf yazıp
/// DI'a <c>services.AddSmsProvider&lt;MyAdapter&gt;()</c> ile kaydetmek ve Sms:Provider değerini
/// adaptörün <see cref="Name"/> değeriyle eşleştirmek yeterlidir.
/// </summary>
public interface ISmsProviderAdapter
{
    string Name { get; }
    Task SendAsync(SmsMessage message, SmsOptions options, CancellationToken cancellationToken);
}

/// <summary>Yapılandırmaya göre kayıtlı adaptörü seçer. Sağlayıcı yoksa sahte gönderim yapmaz.</summary>
public sealed class SmsService(IEnumerable<ISmsProviderAdapter> adapters, IOptions<SmsOptions> options, ILogger<SmsService> logger) : ISmsService
{
    private readonly ISmsProviderAdapter? _adapter = string.IsNullOrWhiteSpace(options.Value.Provider)
        ? null
        : adapters.FirstOrDefault(a => string.Equals(a.Name, options.Value.Provider, StringComparison.OrdinalIgnoreCase));

    public bool IsConfigured => _adapter is not null;

    public async Task SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        if (_adapter is null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(options.Value.Provider)
                ? "SMS sağlayıcısı yapılandırılmamış (Sms:Provider)."
                : $"'{options.Value.Provider}' adlı SMS sağlayıcı adaptörü bulunamadı.");

        await _adapter.SendAsync(message with { PhoneNumber = NormalizePhone(message.PhoneNumber) }, options.Value, cancellationToken);
        logger.LogInformation("SMS gönderildi. Sağlayıcı: {Provider}", _adapter.Name);
    }

    /// <summary>Türkiye numaralarını 905XXXXXXXXX biçimine getirir.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length == 12) return digits;
        if (digits.StartsWith('0') && digits.Length == 11) return "9" + digits;
        if (digits.Length == 10 && digits.StartsWith('5')) return "90" + digits;
        return digits;
    }
}
