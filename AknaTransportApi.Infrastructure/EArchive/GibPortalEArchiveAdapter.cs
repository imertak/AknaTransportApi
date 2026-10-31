using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.EArchive;

/// <summary>
/// GİB e-Arşiv portalı (earsivportal.efatura.gov.tr) adaptörü. Akış, açık kaynak f/fatura kütüphanesindeki
/// portal çağrılarını temel alır:
/// 1. Portal kullanıcısıyla giriş (assos-login) ve taslak fatura oluşturma (EARSIV_PORTAL_FATURA_OLUSTUR).
/// 2. Taslak, yetkilinin kayıtlı telefonuna gönderilen SMS koduyla imzalanır. İmza başarılı olmadan
///    fatura kesilmiş sayılmaz.
/// Kimlik bilgileri yalnızca environment variable ile verilir (EArchive__Username, EArchive__Password).
/// </summary>
public sealed class GibPortalEArchiveAdapter : IEArchiveSmsSigningAdapter
{
    public const string ProviderName = "GibPortal";
    private const string ProductionUrl = "https://earsivportal.efatura.gov.tr";
    private const string TestUrl = "https://earsivportaltest.efatura.gov.tr";
    private const string Signed = "Onaylandı";
    private const string Unsigned = "Onaylanmadı";
    private static readonly TimeSpan SmsSessionLifetime = TimeSpan.FromMinutes(10);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly HttpClient _http;
    private readonly EArchiveOptions _options;
    private readonly IMemoryCache _cache;
    private readonly IBusinessCalendar _calendar;
    private readonly TimeProvider _clock;
    private readonly ILogger<GibPortalEArchiveAdapter> _logger;

    public GibPortalEArchiveAdapter(HttpClient http, IOptions<EArchiveOptions> options, IMemoryCache cache, IBusinessCalendar calendar,
        TimeProvider clock, ILogger<GibPortalEArchiveAdapter> logger)
    {
        _options = options.Value;
        _http = http;
        var baseUrl = !string.IsNullOrWhiteSpace(_options.ApiUrl) ? _options.ApiUrl : _options.UseTestEnvironment ? TestUrl : ProductionUrl;
        _http.BaseAddress ??= new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        _cache = cache;
        _calendar = calendar;
        _clock = clock;
        _logger = logger;
    }

    public string Name => ProviderName;

    public async Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken ct)
    {
        var data = BuildInvoiceData(request);
        var recipient = (string)data["vknTckn"]!;
        return await WithSessionAsync(async token =>
        {
            var before = (await ListDraftsAsync(token, request.InvoiceDate, ct)).Select(Ettn).ToHashSet();

            var created = await DispatchAsync(token, "EARSIV_PORTAL_FATURA_OLUSTUR", "RG_BASITFATURA", data, ct);
            // Bu komut hata durumunda da "error" alanı göndermez; sonuç serbest metindedir.
            var text = created["data"]?.GetValueKind() == JsonValueKind.String ? created["data"]!.GetValue<string>() : string.Empty;
            if (!text.Contains("başarıyla oluşturulmuştur", StringComparison.OrdinalIgnoreCase))
                return new EArchiveSendResult(false, EArchiveDocumentStatus.Rejected, null, null, null,
                    string.IsNullOrWhiteSpace(text) ? "GİB taslak faturayı oluşturmadı." : $"GİB: {text}");

            // ETTN sunucuda üretilir: oluşturma öncesi ve sonrası taslak listesi karşılaştırılır.
            var after = await ListDraftsAsync(token, request.InvoiceDate, ct);
            var draft = after.FirstOrDefault(i => !before.Contains(Ettn(i)) && Str(i, "aliciVknTckn") == recipient)
                        ?? after.FirstOrDefault(i => !before.Contains(Ettn(i)));
            if (draft is null)
                return new EArchiveSendResult(false, EArchiveDocumentStatus.Rejected, null, null, null,
                    "GİB taslağı oluşturdu ancak ETTN bulunamadı. Portaldaki taslakları kontrol edin, çift kayıt oluşturmayın.");

            _logger.LogInformation("GİB e-Arşiv taslağı oluşturuldu. InvoiceId: {InvoiceId}, ETTN: {Ettn}", request.InvoiceId, Ettn(draft));
            // Taslak henüz resmi değildir: SMS ile imzalanana kadar Processing.
            return new EArchiveSendResult(true, EArchiveDocumentStatus.Processing, Ettn(draft), Str(draft, "belgeNumarasi"), null, null);
        }, ct);
    }

    public Task<EArchiveStatusResult> GetStatusAsync(EArchiveDocumentKey key, CancellationToken ct) =>
        WithSessionAsync(token => FindStatusAsync(token, key, ct), ct);

    public Task<byte[]> GetPdfAsync(EArchiveDocumentKey key, CancellationToken ct) =>
        throw new NotSupportedException("GİB e-Arşiv portalı PDF üretmez; belge görüntüsü HTML olarak alınır.");

    /// <summary>
    /// İmzalı fatura portalda doğrudan silinemez: GİB'e iptal talebi oluşturulur (alıcı onayı veya süre dolumu ile kesinleşir).
    /// </summary>
    public Task<EArchiveCancelResult> CancelAsync(EArchiveDocumentKey key, string reason, CancellationToken ct) =>
        WithSessionAsync(async token =>
        {
            await DispatchAsync(token, "EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR", "RG_TASLAKLAR", new JsonObject
            {
                ["ettn"] = key.Reference,
                ["onayDurumu"] = Signed,
                ["belgeTuru"] = "FATURA",
                ["talepAciklama"] = reason,
            }, ct);
            return new EArchiveCancelResult(true, null);
        }, ct);

    public Task<EArchiveSmsChallenge> StartSmsSigningAsync(EArchiveDocumentKey key, CancellationToken ct) =>
        WithSessionAsync(async token =>
        {
            var phone = (await DispatchAsync(token, "EARSIV_PORTAL_TELEFONNO_SORGULA", "RG_SMSONAY", new JsonObject(), ct))["data"]?["telefon"]?.ToString();
            if (string.IsNullOrWhiteSpace(phone))
                throw new IntegrationUnavailableException("GİB portalında imza için kayıtlı cep telefonu yok. Portal > Kullanıcı bilgilerinden telefon ekleyin.");

            var sms = await DispatchAsync(token, "EARSIV_PORTAL_SMSSIFRE_GONDER", "RG_SMSONAY", new JsonObject
            {
                ["CEPTEL"] = phone,
                ["KCEPTEL"] = false,
                ["TIP"] = "",
            }, ct);
            var operationId = sms["data"]?["oid"]?.ToString() ?? sms["oid"]?.ToString();
            if (string.IsNullOrWhiteSpace(operationId)) throw new IntegrationUnavailableException("GİB SMS kodu gönderemedi.");

            // SMS doğrulaması aynı portal oturumunda yapılmalıdır: token kısa süre saklanır, oturum kapatılmaz.
            _cache.Set(CacheKey(operationId), new SmsSession(token, key.Reference), SmsSessionLifetime);
            return new EArchiveSmsChallenge(operationId, MaskPhone(phone));
        }, ct, keepSession: true);

    public async Task<EArchiveStatusResult> CompleteSmsSigningAsync(EArchiveDocumentKey key, string operationId, string code, CancellationToken ct)
    {
        if (!_cache.TryGetValue<SmsSession>(CacheKey(operationId), out var session) || session is null || session.Reference != key.Reference)
            return new EArchiveStatusResult(EArchiveDocumentStatus.Processing, null, null, "SMS oturumunun süresi doldu. Yeni kod isteyin.");

        var keepSession = false;
        try
        {
            var current = await FindStatusAsync(session.Token, key, ct);
            if (current.Status != EArchiveDocumentStatus.Processing) return current;

            var draft = (await ListDraftsAsync(session.Token, key.InvoiceDate, ct)).First(i => Ettn(i) == key.Reference);
            var response = await DispatchAsync(session.Token, _options.GibSmsVerifyCommand, "RG_SMSONAY", new JsonObject
            {
                ["SIFRE"] = code.Trim(),
                ["OID"] = operationId,
                // OPR ve DATA olmadan GİB kodu doğrular ama faturayı imzalamaz.
                ["OPR"] = 1,
                ["DATA"] = new JsonArray(draft.DeepClone()),
            }, ct);
            if (response["data"]?["sonuc"]?.ToString() != "1")
            {
                // Yanlış kod: aynı SMS ile tekrar denenebilsin diye portal oturumu açık kalır (süre dolana kadar).
                keepSession = true;
                return new EArchiveStatusResult(EArchiveDocumentStatus.Processing, null, null, "SMS kodu doğrulanamadı veya fatura imzalanmadı. Kodu kontrol edip tekrar deneyin.");
            }

            // İmza ancak portal listesinde "Onaylandı" görünürse kabul edilir.
            var after = await FindStatusAsync(session.Token, key, ct);
            if (after.Status != EArchiveDocumentStatus.Issued)
                return after with { ErrorMessage = after.ErrorMessage ?? "GİB imzayı onayladı ancak belge henüz onaylı görünmüyor. Birazdan durum otomatik güncellenecek." };
            _logger.LogInformation("GİB e-Arşiv faturası imzalandı. ETTN: {Ettn}", key.Reference);
            return after;
        }
        finally
        {
            if (!keepSession)
            {
                _cache.Remove(CacheKey(operationId));
                await LogoutAsync(session.Token);
            }
        }
    }

    public Task<EArchiveCancelResult> DiscardDraftAsync(EArchiveDocumentKey key, string reason, CancellationToken ct) =>
        WithSessionAsync(async token =>
        {
            var draft = (await ListDraftsAsync(token, key.InvoiceDate, ct)).FirstOrDefault(i => Ettn(i) == key.Reference);
            if (draft is null) return new EArchiveCancelResult(true, null); // portalda zaten yok
            if (Str(draft, "onayDurumu") == Signed)
                return new EArchiveCancelResult(false, "Fatura portalda imzalanmış; taslak olarak silinemez.");
            await DispatchAsync(token, "EARSIV_PORTAL_FATURA_SIL", "RG_BASITTASLAKLAR", new JsonObject
            {
                ["silinecekler"] = new JsonArray(draft.DeepClone()),
                ["aciklama"] = reason,
            }, ct);
            return new EArchiveCancelResult(true, null);
        }, ct);

    public Task<string> GetHtmlAsync(EArchiveDocumentKey key, bool signed, CancellationToken ct) =>
        WithSessionAsync(async token =>
        {
            var response = await DispatchAsync(token, "EARSIV_PORTAL_FATURA_GOSTER", "RG_BASITTASLAKLAR", new JsonObject
            {
                ["ettn"] = key.Reference,
                ["onayDurumu"] = signed ? Signed : Unsigned,
            }, ct);
            return response["data"]?.ToString() ?? throw new IntegrationUnavailableException("GİB belge görüntüsünü döndürmedi.");
        }, ct);

    // ─── Fatura verisi ──────────────────────────────────────────────────────────

    /// <summary>Portalın "Basit Fatura" ekranının gönderdiği JSON. Alan adları portalın kendi anahtarlarıdır.</summary>
    internal JsonObject BuildInvoiceData(EArchiveInvoiceRequest r)
    {
        var taxNumber = new string((r.Customer.TaxNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (taxNumber.Length is not (10 or 11))
            throw new BusinessValidationException("e-Arşiv faturası için müşterinin 10 haneli VKN'si veya 11 haneli TCKN'si kayıtlı olmalıdır.",
                [new ErrorDetail("customer.taxNumber", "Geçerli VKN/TCKN gerekli.")]);

        var withholding = WithholdingTypes.Find(r.WithholdingCode);
        if (r.WithholdingCode is not null && withholding is null)
            throw new BusinessValidationException($"Geçersiz tevkifat kodu: {r.WithholdingCode}");

        var now = _calendar.ToLocal(_clock.GetUtcNow().UtcDateTime);
        var (firstName, lastName) = SplitName(r.Customer.Name);
        var isPerson = taxNumber.Length == 11;

        // Satır KDV ve tevkifatı, toplamlarla kuruşu kuruşuna tutsun diye son satırda dengelenir.
        var rows = new JsonArray();
        decimal vatLeft = r.VatAmount, whLeft = r.WithholdingAmount;
        for (var i = 0; i < r.Lines.Count; i++)
        {
            var line = r.Lines[i];
            var last = i == r.Lines.Count - 1;
            var vat = last ? vatLeft : Money.CalculateVat(line.LineTotal, line.VatRate);
            vatLeft -= vat;
            var row = new JsonObject
            {
                ["iskontoArttm"] = "İskonto",
                ["malHizmet"] = Truncate(line.Description, 500),
                ["miktar"] = line.Quantity,
                ["birim"] = "C62",
                ["birimFiyat"] = Amount(line.UnitPrice),
                ["fiyat"] = Amount(line.LineTotal),
                ["iskontoOrani"] = 0,
                ["iskontoTutari"] = "0.00",
                ["iskontoNedeni"] = "",
                ["malHizmetTutari"] = Amount(line.LineTotal),
                ["kdvOrani"] = line.VatRate.ToString("0", Inv),
                ["vergiOrani"] = 0,
                ["kdvTutari"] = Amount(vat),
                ["vergininKdvTutari"] = "0.00",
                ["ozelMatrahTutari"] = "0",
            };
            if (withholding is not null)
            {
                var wh = last ? whLeft : Money.Round(vat * withholding.Rate / 100m);
                whLeft -= wh;
                row["tevkifatKodu"] = int.Parse(withholding.Code, Inv);
                row[$"V{WithholdingTypes.TaxCode}Orani"] = withholding.Rate;
                row[$"V{WithholdingTypes.TaxCode}Tutari"] = Amount(wh);
                row[$"V{WithholdingTypes.TaxCode}KdvTutari"] = "0.00";
            }
            rows.Add(row);
        }

        var data = new JsonObject
        {
            ["faturaUuid"] = "", // ETTN'i GİB üretir.
            ["belgeNumarasi"] = "",
            ["faturaTarihi"] = r.InvoiceDate.ToString("dd/MM/yyyy", Inv),
            ["saat"] = now.ToString("HH:mm:ss", Inv),
            ["paraBirimi"] = r.Currency,
            ["dovzTLkur"] = "0",
            ["faturaTipi"] = withholding is null ? "SATIS" : "TEVKIFAT",
            ["hangiTip"] = "5000/30000",
            ["siparisNumarasi"] = "", ["siparisTarihi"] = "",
            ["irsaliyeNumarasi"] = "", ["irsaliyeTarihi"] = "",
            ["fisNo"] = "", ["fisTarihi"] = "", ["fisSaati"] = " ", ["fisTipi"] = " ",
            ["zRaporNo"] = "", ["okcSeriNo"] = "",
            ["vknTckn"] = taxNumber,
            ["aliciUnvan"] = isPerson ? "" : Truncate(r.Customer.Name, 250),
            ["aliciAdi"] = isPerson ? firstName : "",
            ["aliciSoyadi"] = isPerson ? lastName : "",
            ["bulvarcaddesokak"] = Truncate(r.Customer.Address ?? "", 500),
            ["binaAdi"] = "", ["binaNo"] = "", ["kapiNo"] = "", ["kasabaKoy"] = "",
            ["mahalleSemtIlce"] = "",
            ["sehir"] = " ",
            ["ulke"] = "Türkiye",
            ["postaKodu"] = "",
            ["tel"] = r.Customer.PhoneNumber ?? "",
            ["fax"] = "",
            ["eposta"] = r.Customer.Email ?? "",
            ["websitesi"] = "",
            ["vergiDairesi"] = r.Customer.TaxOffice ?? "",
            ["iadeTable"] = new JsonArray(),
            ["ozelMatrahTutari"] = "0",
            ["ozelMatrahOrani"] = 0,
            ["ozelMatrahVergiTutari"] = "0.00",
            ["vergiCesidi"] = " ",
            ["malHizmetTable"] = rows,
            ["tip"] = "İskonto",
            ["matrah"] = Amount(r.SubTotal),
            ["malhizmetToplamTutari"] = Amount(r.SubTotal),
            ["toplamIskonto"] = "0.00",
            ["hesaplanankdv"] = Amount(r.VatAmount),
            // Tevkifat stopaj niteliğindedir: vergiler toplamına girmez, ödenecek tutardan düşülür.
            ["vergilerToplami"] = Amount(r.VatAmount),
            ["vergilerDahilToplamTutar"] = Amount(r.GrandTotal),
            ["toplamMasraflar"] = "0",
            ["odenecekTutar"] = Amount(r.PayableAmount ?? r.GrandTotal - r.WithholdingAmount),
            ["not"] = Truncate(r.Notes ?? "", 2000),
        };
        return data;
    }

    // ─── Portal HTTP ────────────────────────────────────────────────────────────

    private async Task<T> WithSessionAsync<T>(Func<string, Task<T>> action, CancellationToken ct, bool keepSession = false)
    {
        var token = await LoginAsync(ct);
        var keep = false;
        try
        {
            var result = await action(token);
            keep = keepSession;
            return result;
        }
        finally
        {
            if (!keep) await LogoutAsync(token);
        }
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
            throw new IntegrationUnavailableException("GİB e-Arşiv portal kullanıcı bilgileri eksik (EArchive__Username, EArchive__Password).");

        using var response = await _http.PostAsync("earsiv-services/assos-login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["assoscmd"] = _options.UseTestEnvironment ? "login" : "anologin",
            ["rtype"] = "json",
            ["userid"] = _options.Username,
            ["sifre"] = _options.Password,
            ["sifre2"] = _options.Password,
            ["parola"] = "1",
        }), ct);
        var json = await ReadJsonAsync(response, ct);
        var token = json["token"]?.ToString();
        if (string.IsNullOrWhiteSpace(token)) throw new IntegrationUnavailableException($"GİB portal girişi başarısız: {ErrorText(json) ?? "token alınamadı"}");
        return token;
    }

    private async Task LogoutAsync(string token)
    {
        try
        {
            using var response = await _http.PostAsync("earsiv-services/assos-login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["assoscmd"] = "logout",
                ["rtype"] = "json",
                ["token"] = token,
            }));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "GİB portal oturumu kapatılamadı.");
        }
    }

    internal async Task<JsonObject> DispatchAsync(string token, string command, string pageName, JsonNode payload, CancellationToken ct)
    {
        using var response = await _http.PostAsync("earsiv-services/dispatch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["cmd"] = command,
            ["callid"] = Guid.NewGuid().ToString(),
            ["pageName"] = pageName,
            ["token"] = token,
            ["jp"] = payload.ToJsonString(),
        }), ct);
        var json = await ReadJsonAsync(response, ct);
        if (json["error"]?.ToString() is { } error && error != "0")
            throw new IntegrationUnavailableException($"GİB e-Arşiv hatası: {ErrorText(json) ?? "bilinmeyen hata"}");
        return json;
    }

    private async Task<List<JsonObject>> ListDraftsAsync(string token, DateOnly date, CancellationToken ct)
    {
        var day = date.ToString("dd/MM/yyyy", Inv);
        var response = await DispatchAsync(token, "EARSIV_PORTAL_TASLAKLARI_GETIR", "RG_BASITTASLAKLAR", new JsonObject
        {
            ["baslangic"] = day,
            ["bitis"] = day,
            ["hangiTip"] = "5000/30000",
            ["table"] = new JsonArray(),
        }, ct);
        return response["data"] is JsonArray items ? items.OfType<JsonObject>().ToList() : [];
    }

    private async Task<EArchiveStatusResult> FindStatusAsync(string token, EArchiveDocumentKey key, CancellationToken ct)
    {
        var item = (await ListDraftsAsync(token, key.InvoiceDate, ct)).FirstOrDefault(i => Ettn(i) == key.Reference);
        if (item is null)
            return new EArchiveStatusResult(EArchiveDocumentStatus.Rejected, null, null, "Belge GİB portalında bulunamadı (silinmiş olabilir).");
        return Str(item, "onayDurumu") switch
        {
            Signed => new EArchiveStatusResult(EArchiveDocumentStatus.Issued, Str(item, "belgeNumarasi"), null, null),
            "Silinmiş" => new EArchiveStatusResult(EArchiveDocumentStatus.Rejected, null, null, "Taslak GİB portalında silinmiş."),
            _ => new EArchiveStatusResult(EArchiveDocumentStatus.Processing, Str(item, "belgeNumarasi"), null, null),
        };
    }

    private static async Task<JsonObject> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new IntegrationUnavailableException($"GİB e-Arşiv portalı yanıt vermedi ({(int)response.StatusCode}).");
        try
        {
            return JsonNode.Parse(body) as JsonObject ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new IntegrationUnavailableException("GİB e-Arşiv portalından beklenmeyen yanıt alındı.");
        }
    }

    private static string? ErrorText(JsonObject json) => json["messages"] is JsonArray { Count: > 0 } messages
        ? messages[0] switch
        {
            JsonObject o => o["text"]?.ToString(),
            JsonValue v => v.ToString(),
            _ => null,
        }
        : null;

    private static string Ettn(JsonObject item) => Str(item, "ettn") ?? string.Empty;
    private static string? Str(JsonObject item, string key) => item[key]?.ToString();
    private static string Amount(decimal value) => Money.Round(value).ToString("0.00", Inv);
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string CacheKey(string operationId) => $"gib-sms:{operationId}";

    private static (string First, string Last) SplitName(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? (name.Trim(), "") : (string.Join(' ', parts[..^1]), parts[^1]);
    }

    internal static string MaskPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        return digits.Length < 4 ? "****" : $"*** *** {digits[^4..^2]} {digits[^2..]}";
    }

    private sealed record SmsSession(string Token, string Reference);
}
