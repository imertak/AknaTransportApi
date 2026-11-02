using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.Email;

/// <summary>
/// Resend (https://resend.com) HTTP API ile e-posta gönderir. Gönderen adresin alan adı Resend panelinde doğrulanmış olmalıdır.
/// Başarısız yanıtta istisna fırlatır; outbox yeniden dener. Idempotency-Key ile aynı e-posta iki kez gönderilmez.
/// </summary>
public sealed class ResendEmailService(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailService> logger) : IEmailService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ResendApiKey) && !string.IsNullOrWhiteSpace(_options.FromEmail);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("E-posta sağlayıcısı yapılandırılmamış (Email:ResendApiKey / Email:FromEmail).");

        var cc = _options.CcFor(message);
        var payload = new ResendRequest(
            From: string.IsNullOrWhiteSpace(_options.FromName) ? _options.FromEmail! : $"{_options.FromName} <{_options.FromEmail}>",
            To: [message.To],
            Cc: cc.Count == 0 ? null : cc,
            Subject: message.Subject,
            Html: message.HtmlBody,
            Text: message.TextBody,
            Attachments: message.Attachments.Count == 0
                ? null
                : message.Attachments.Select(a => new ResendAttachment(a.FileName, Convert.ToBase64String(a.Content), a.ContentType, a.ContentId)).ToList());

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.ResendApiUrl.TrimEnd('/')}/emails")
        {
            Content = JsonContent.Create(payload, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
        if (!string.IsNullOrWhiteSpace(message.IdempotencyKey)) request.Headers.Add("Idempotency-Key", message.IdempotencyKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var response = await http.SendAsync(request, timeout.Token);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Yanıt gövdesi Resend'in hata mesajıdır (ör. doğrulanmamış alan adı); anahtar içermez.
            throw new InvalidOperationException($"Resend e-postayı kabul etmedi ({(int)response.StatusCode}): {Truncate(body, 500)}");
        }

        string? id = null;
        try { id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString(); } catch (Exception) { /* id yalnızca log içindir */ }
        logger.LogInformation("E-posta Resend ile gönderildi. Konu: {Subject}, ResendId: {ResendId}", message.Subject, id);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed record ResendRequest(string From, IReadOnlyList<string> To, IReadOnlyList<string>? Cc, string Subject, string Html, string Text, IReadOnlyList<ResendAttachment>? Attachments);

    /// <param name="ContentId">Satır içi görsel (HTML'de cid:...) için Resend'in content_id alanı.</param>
    private sealed record ResendAttachment(string Filename, string Content, [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("content_id")] string? ContentId = null);
}
