using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Infrastructure.EArchive;

/// <summary>
/// HTTP/JSON tabanlı e-Arşiv entegratörleri için temel sınıf. Kimlik doğrulama, zaman aşımı ve
/// hata yönetimi burada; sağlayıcıya özgü uç noktalar ve veri dönüşümleri alt sınıfta yazılır.
///
/// Yeni sağlayıcı ekleme:
/// 1. Bu sınıftan türeyen bir adaptör yazın (ör. UyumsoftEArchiveAdapter) ve sağlayıcı dokümantasyonundaki
///    istek/yanıt modellerini uygulayın.
/// 2. Infrastructure DI'da <c>services.AddEArchiveProvider&lt;UyumsoftEArchiveAdapter&gt;()</c> çağırın.
/// 3. EArchive__Provider=Uyumsoft ve kimlik bilgilerini environment variable olarak verin.
/// </summary>
public abstract class HttpEArchiveProviderAdapterBase(HttpClient httpClient, IOptions<EArchiveOptions> options) : IEArchiveProviderAdapter
{
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected HttpClient Http { get; } = Configure(httpClient, options.Value);
    protected EArchiveOptions Options { get; } = options.Value;

    public abstract string Name { get; }
    public abstract Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken);
    public abstract Task<EArchiveStatusResult> GetStatusAsync(EArchiveDocumentKey key, CancellationToken cancellationToken);
    public abstract Task<byte[]> GetPdfAsync(EArchiveDocumentKey key, CancellationToken cancellationToken);
    public abstract Task<EArchiveCancelResult> CancelAsync(EArchiveDocumentKey key, string reason, CancellationToken cancellationToken);

    protected async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        using var response = await Http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct)
               ?? throw new InvalidOperationException($"{Name}: boş yanıt alındı.");
    }

    protected async Task<byte[]> GetBytesAsync(string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Length > 500) body = body[..500];
        throw new HttpRequestException($"{Name} e-Arşiv hatası ({(int)response.StatusCode}): {body}", null, response.StatusCode);
    }

    private static HttpClient Configure(HttpClient client, EArchiveOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiUrl)) client.BaseAddress = new Uri(options.ApiUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        return client;
    }
}
