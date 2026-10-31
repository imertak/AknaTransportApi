using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Infrastructure;

/// <summary>
/// GİB e-Arşiv portalının (earsiv-services) davranışını taklit eden HTTP işleyicisi. Yanıt biçimleri
/// f/fatura kütüphanesinin beklediği portal yanıtlarıyla aynıdır. Gerçek portala istek gitmez.
/// </summary>
public sealed class FakeGibPortal : HttpMessageHandler
{
    public const string User = "33333301";
    public const string Password = "1";
    public const string SmsCode = "123456";

    private readonly object _gate = new();
    private int _counter;

    public List<JsonObject> Drafts { get; } = [];
    public List<JsonObject> Created { get; } = [];
    public List<string> Commands { get; } = [];
    public int ActiveSessions { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => WebUtility.UrlDecode(p[0]), p => p.Length > 1 ? WebUtility.UrlDecode(p[1]) : "");

        lock (_gate)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/assos-login", StringComparison.Ordinal)) return Json(Login(form));
            if (path.EndsWith("/dispatch", StringComparison.Ordinal))
            {
                if (form.GetValueOrDefault("token") != "tkn") return Json(Error("Oturum geçersiz"));
                Commands.Add(form["cmd"]);
                return Json(Dispatch(form["cmd"], JsonNode.Parse(form["jp"])!));
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private JsonObject Login(Dictionary<string, string> form)
    {
        if (form["assoscmd"] == "logout")
        {
            ActiveSessions = Math.Max(0, ActiveSessions - 1);
            return new JsonObject { ["data"] = "ok" };
        }
        if (form.GetValueOrDefault("userid") != User || form.GetValueOrDefault("sifre") != Password)
            return Error("Kullanıcı adı veya şifre hatalı");
        ActiveSessions++;
        return new JsonObject { ["token"] = "tkn" };
    }

    private JsonObject Dispatch(string cmd, JsonNode jp)
    {
        switch (cmd)
        {
            case "EARSIV_PORTAL_TASLAKLARI_GETIR":
                return new JsonObject { ["data"] = new JsonArray(Drafts.Select(d => (JsonNode)d.DeepClone()).ToArray()) };

            case "EARSIV_PORTAL_FATURA_OLUSTUR":
                Created.Add(jp.AsObject().DeepClone().AsObject());
                _counter++;
                Drafts.Add(new JsonObject
                {
                    ["ettn"] = Guid.NewGuid().ToString(),
                    ["belgeNumarasi"] = $"GIB2026{_counter:000000000}",
                    ["aliciVknTckn"] = jp["vknTckn"]!.ToString(),
                    ["aliciUnvanAdSoyad"] = jp["aliciUnvan"]!.ToString(),
                    ["belgeTarihi"] = jp["faturaTarihi"]!.ToString().Replace('/', '-'),
                    ["belgeTuru"] = "FATURA",
                    ["onayDurumu"] = "Onaylanmadı",
                });
                return new JsonObject { ["data"] = "Faturanız başarıyla oluşturulmuştur. Düzenlenen Belgeler menüsünden faturanıza ulaşabilirsiniz." };

            case "EARSIV_PORTAL_TELEFONNO_SORGULA":
                return new JsonObject { ["data"] = new JsonObject { ["telefon"] = "5551234567" } };

            case "EARSIV_PORTAL_SMSSIFRE_GONDER":
                return new JsonObject { ["data"] = new JsonObject { ["oid"] = "oid-" + Guid.NewGuid().ToString("N")[..8] } };

            case "0lhozfib5410mp":
                if (jp["SIFRE"]?.ToString() != SmsCode || jp["OPR"]?.GetValue<int>() != 1 || jp["DATA"] is not JsonArray { Count: > 0 } data)
                    return new JsonObject { ["data"] = new JsonObject { ["sonuc"] = "0" } };
                foreach (var item in data)
                    if (Drafts.FirstOrDefault(d => d["ettn"]!.ToString() == item!["ettn"]!.ToString()) is { } draft)
                        draft["onayDurumu"] = "Onaylandı";
                return new JsonObject { ["data"] = new JsonObject { ["sonuc"] = "1" } };

            case "EARSIV_PORTAL_FATURA_SIL":
                foreach (var item in jp["silinecekler"]!.AsArray())
                    Drafts.RemoveAll(d => d["ettn"]!.ToString() == item!["ettn"]!.ToString());
                return new JsonObject { ["data"] = "1 adet fatura silindi." };

            case "EARSIV_PORTAL_FATURA_GOSTER":
                return new JsonObject { ["data"] = $"<html><body>e-Arşiv fatura {jp["ettn"]}</body></html>" };

            case "EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR":
                return new JsonObject { ["data"] = "İptal talebiniz oluşturuldu." };

            default:
                return Error("Service Not Found");
        }
    }

    private static JsonObject Error(string text) => new()
    {
        ["error"] = "1",
        ["messages"] = new JsonArray(new JsonObject { ["type"] = "4", ["text"] = text }),
    };

    private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}

/// <summary>API'yi GİB e-Arşiv portal adaptörüyle, sahte portala bağlı olarak çalıştırır.</summary>
public sealed class GibPortalApiFactory : AknaApiFactory
{
    public const string CompanyIban = "TR330006100519786457841326";

    public FakeGibPortal Portal { get; } = new();

    protected override string EArchiveProvider => GibPortalEArchiveAdapter.ProviderName;

    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["EArchive:Username"] = FakeGibPortal.User,
        ["EArchive:Password"] = FakeGibPortal.Password,
        ["EArchive:UseTestEnvironment"] = "true",
        ["App:CompanyIban"] = CompanyIban,
        ["App:CompanyBankName"] = "Test Bankası",
    };

    protected override void ConfigureExtraServices(IServiceCollection services) =>
        services.AddHttpClient<GibPortalEArchiveAdapter>().ConfigurePrimaryHttpMessageHandler(() => Portal);
}
