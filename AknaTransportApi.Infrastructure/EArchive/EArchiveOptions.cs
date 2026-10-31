namespace AknaTransportApi.Infrastructure.EArchive;

/// <summary>
/// e-Arşiv entegratörü ayarları. Kimlik bilgileri environment variable ile verilir:
/// EArchive__Provider, EArchive__Username, EArchive__Password, EArchive__UseTestEnvironment (GİB portalı);
/// EArchive__ApiUrl, EArchive__ApiKey (özel entegratörler).
/// </summary>
public sealed class EArchiveOptions
{
    public const string SectionName = "EArchive";

    /// <summary>
    /// Kullanılacak adaptör adı. Yerleşik: "GibPortal" (GİB e-Arşiv portalı, SMS ile imza).
    /// Özel entegratörler için adaptör eklenebilir. Boşsa e-Arşiv devre dışıdır.
    /// </summary>
    public string? Provider { get; set; }
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Faturayı düzenleyen firmanın VKN'si.</summary>
    public string? SupplierTaxNumber { get; set; }
    /// <summary>e-Arşiv fatura seri ön eki (sağlayıcıda tanımlı, ör. "AKN").</summary>
    public string? InvoiceSeries { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>true ise sağlayıcının test ortamı kullanılır (GİB: earsivportaltest.efatura.gov.tr).</summary>
    public bool UseTestEnvironment { get; set; }

    /// <summary>
    /// GİB portalının SMS doğrulama/imza komut kimliği. GİB bu servisi okunabilir addan opak bir kimliğe
    /// taşıdı; değişirse koda dokunmadan güncellenebilsin diye ayardır.
    /// </summary>
    public string GibSmsVerifyCommand { get; set; } = "0lhozfib5410mp";
}
