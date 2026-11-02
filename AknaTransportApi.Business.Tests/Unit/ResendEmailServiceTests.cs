using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Tests.Unit;

public class ResendEmailServiceTests
{
    private sealed class CaptureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static ResendEmailService Create(CaptureHandler handler, string? apiKey = "re_test_key") =>
        new(new HttpClient(handler), Options.Create(new EmailOptions { Provider = "Resend", ResendApiKey = apiKey, FromEmail = "fatura@akna.com.tr", FromName = "AKNA Transport" }),
            NullLogger<ResendEmailService>.Instance);

    private static EmailMessage Message() => new("musteri@example.com", "Müşteri", "Faturanız", "<p>Merhaba</p>", "Merhaba",
        [new EmailAttachment("Fatura-1.pdf", [1, 2, 3])], IdempotencyKey: "invoice-email-abc");

    [Fact]
    public async Task Sends_to_resend_with_attachments_and_idempotency_key()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, """{"id":"email_123"}""");
        await Create(handler).SendAsync(Message());

        Assert.Equal("https://api.resend.com/emails", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("re_test_key", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal("invoice-email-abc", handler.Request.Headers.GetValues("Idempotency-Key").Single());

        var json = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("AKNA Transport <fatura@akna.com.tr>", json.GetProperty("from").GetString());
        Assert.Equal("musteri@example.com", json.GetProperty("to")[0].GetString());
        Assert.Equal("Faturanız", json.GetProperty("subject").GetString());
        var attachment = json.GetProperty("attachments")[0];
        Assert.Equal("Fatura-1.pdf", attachment.GetProperty("filename").GetString());
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), attachment.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Error_response_throws_so_outbox_retries()
    {
        var handler = new CaptureHandler(HttpStatusCode.Forbidden, """{"statusCode":403,"message":"The akna.com.tr domain is not verified."}""");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).SendAsync(Message()));
        Assert.Contains("403", ex.Message);
        Assert.Contains("not verified", ex.Message);
    }

    [Fact]
    public void Not_configured_without_api_key() => Assert.False(Create(new CaptureHandler(HttpStatusCode.OK, "{}"), apiKey: null).IsConfigured);

    [Fact]
    public void AlwaysCc_is_added_once_and_never_duplicates_recipient()
    {
        var options = new EmailOptions { AlwaysCc = "imertakpinar0@gmail.com; Muhasebe@firma.com" };
        var message = new EmailMessage("muhasebe@firma.com", null, "Konu", "<p>x</p>", "x", []) { Cc = ["satin@firma.com", "IMERTAKPINAR0@gmail.com"] };

        Assert.Equal(["satin@firma.com", "IMERTAKPINAR0@gmail.com"], options.CcFor(message));
        Assert.Equal(["imertakpinar0@gmail.com"], new EmailOptions { AlwaysCc = "imertakpinar0@gmail.com" }.CcFor(message with { Cc = [] }));
    }
}

public class ChromeHtmlPdfRendererTests
{
    [Fact]
    public async Task Missing_browser_is_reported_as_unavailable()
    {
        var renderer = new ChromeHtmlPdfRenderer(Options.Create(new HtmlPdfOptions { ChromePath = "/yok/chrome" }), NullLogger<ChromeHtmlPdfRenderer>.Instance);
        Assert.False(renderer.IsAvailable);
        await Assert.ThrowsAsync<IntegrationUnavailableException>(() => renderer.RenderAsync("<p>x</p>", CancellationToken.None));
    }
}
