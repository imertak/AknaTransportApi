using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.Push;

/// <summary>Push bildirimi ayarları (environment variable: Push__Enabled, Push__ExpoAccessToken).</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>false ise telefona bildirim gönderilmez; uygulama içi bildirim listesi yine çalışır.</summary>
    public bool Enabled { get; set; } = true;
    public string ApiUrl { get; set; } = "https://exp.host/--/api/v2/push/send";
    /// <summary>Expo hesabında "Enhanced push security" açıksa gerekir. Kapalıysa boş bırakılabilir.</summary>
    public string? ExpoAccessToken { get; set; }
    /// <summary>Android bildirim kanalı; uygulamadaki kanal adıyla aynı olmalıdır.</summary>
    public string AndroidChannelId { get; set; } = "jobs";
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// Expo Push API (https://docs.expo.dev/push-notifications/sending-notifications/) ile bildirim gönderir.
/// Expo, iOS (APNs) ve Android (FCM) dağıtımını kendisi yapar. Tek istekte en fazla 100 mesaj gider.
/// Ağ hatası veya 5xx yanıtta istisna fırlatılır; outbox yeniden dener.
/// </summary>
public sealed class ExpoPushNotificationSender(HttpClient http, IOptions<PushOptions> options, ILogger<ExpoPushNotificationSender> logger) : IPushNotificationSender
{
    private const int MaxBatch = 100;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly PushOptions _options = options.Value;

    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.ApiUrl);

    public async Task<PushSendResult> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken ct)
    {
        var invalid = new List<string>();
        var errors = new List<PushTicketError>();
        foreach (var batch in messages.Chunk(MaxBatch))
        {
            var payload = batch.Select(m => new ExpoMessage(m.Token, m.Title, m.Body, m.Data, "default", m.Badge, _options.AndroidChannelId, "high")).ToList();
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.ApiUrl) { Content = JsonContent.Create(payload, options: Json) };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(_options.ExpoAccessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ExpoAccessToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Expo push isteği başarısız ({(int)response.StatusCode}): {Truncate(body, 500)}");

            var batchErrors = ReadTicketErrors(body, batch).ToList();
            errors.AddRange(batchErrors);
            invalid.AddRange(batchErrors.Where(e => e.Error == "DeviceNotRegistered").Select(e => e.Token));
        }
        // InvalidCredentials vb. hatalar bildirimin telefona hiç gitmediği anlamına gelir; günlükte görünmeli.
        foreach (var e in errors.Where(e => e.Error != "DeviceNotRegistered"))
            logger.LogWarning("Expo push hatası: {Error} - {Message} (token: {Token})", e.Error, e.Message, Mask(e.Token));
        logger.LogInformation("Push bildirimi gönderildi. Cihaz: {Count}, Hatalı: {Errors}, Geçersiz: {Invalid}", messages.Count, errors.Count, invalid.Count);
        return new PushSendResult(invalid, errors);
    }

    /// <summary>Yanıttaki her bilet (ticket) mesajlarla aynı sıradadır; DeviceNotRegistered olan token'lar geçersizdir.</summary>
    public static IEnumerable<string> ReadInvalidTokens(string body, IReadOnlyList<PushMessage> batch) =>
        ReadTicketErrors(body, batch).Where(e => e.Error == "DeviceNotRegistered").Select(e => e.Token);

    /// <summary>Yanıttaki status=error biletlerini mesajın token'ıyla eşleştirerek döner.</summary>
    public static IEnumerable<PushTicketError> ReadTicketErrors(string body, IReadOnlyList<PushMessage> batch)
    {
        JsonElement data;
        try
        {
            if (!JsonDocument.Parse(body).RootElement.TryGetProperty("data", out data) || data.ValueKind != JsonValueKind.Array) yield break;
        }
        catch (JsonException)
        {
            yield break;
        }

        var i = 0;
        foreach (var ticket in data.EnumerateArray())
        {
            if (i >= batch.Count) yield break;
            var token = batch[i++].Token;
            if (!ticket.TryGetProperty("status", out var status) || status.GetString() != "error") continue;
            string? code = null;
            if (ticket.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                code = error.GetString();
            var message = ticket.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            yield return new PushTicketError(token, code ?? "Unknown", message);
        }
    }

    private static string Mask(string token) => token.Length <= 24 ? token : token[..24] + "…";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record ExpoMessage(
        string To,
        string Title,
        string Body,
        IReadOnlyDictionary<string, string> Data,
        string Sound,
        int? Badge,
        string ChannelId,
        string Priority);
}
