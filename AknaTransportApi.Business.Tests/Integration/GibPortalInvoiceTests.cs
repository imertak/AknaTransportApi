using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Integration;

/// <summary>GİB e-Arşiv portal adaptörü: tevkifat, yazıyla tutar, IBAN ve SMS ile imza akışı (sahte portal ile).</summary>
public class GibPortalInvoiceTests(GibPortalApiFactory factory) : IClassFixture<GibPortalApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string Admin, Guid JobId)> CompletedJobAsync(string? taxNumber = "1234567890")
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Post, "/api/customers", new CustomerRequest
        {
            CompanyName = $"Lojistik {Guid.NewGuid():N}"[..20],
            Email = "muhasebe@musteri.example",
            PhoneNumber = "02120000000",
            TaxNumber = taxNumber,
            TaxOffice = "Zincirlikuyu",
            Address = "Büyükdere Cad. No:1 Şişli İstanbul",
        }, admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        return (admin, job.Job.Id);
    }

    private async Task<InvoiceDetailDto> IssuedDraftAsync(string admin, Guid jobId, string? withholding = "624")
    {
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [jobId], WithholdingCode = withholding, NoWithholding = withholding is null }, admin);
        return await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
    }

    [Fact]
    public async Task Invoice_has_withholding_amount_in_words_and_iban()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [jobId], WithholdingCode = "624" }, admin);

        // 4000 + %20 KDV (800) = 4800; 624 Yük taşımacılığı 2/10 → 160 tevkifat; ödenecek 4640.
        Assert.Equal(4800m, invoice.GrandTotal);
        Assert.Equal("624", invoice.WithholdingCode);
        Assert.Equal(20m, invoice.WithholdingRate);
        Assert.Equal(160m, invoice.WithholdingAmount);
        Assert.Equal(4640m, invoice.PayableAmount);
        Assert.Equal("YALNIZ: DÖRT BİN ALTI YÜZ KIRK TÜRK LİRASI", invoice.AmountInWords);
        Assert.Equal("TR33 0006 1005 1978 6457 8413 26", invoice.Iban);
        Assert.Equal("Test Bankası", invoice.BankName);
        Assert.Contains("YALNIZ: DÖRT BİN ALTI YÜZ KIRK TÜRK LİRASI", invoice.DocumentNote);
        Assert.Contains("IBAN: TR33 0006 1005 1978 6457 8413 26 (Test Bankası)", invoice.DocumentNote);

        var types = await _client.OkAsync<List<WithholdingTypeDto>>(HttpMethod.Get, "/api/invoices/withholding-types", token: admin);
        Assert.Contains(types, t => t is { Code: "624", Rate: 20, Fraction: "2/10" });

        var (status, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [jobId], WithholdingCode = "999" }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        var (ibanStatus, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [jobId], Iban = "TR000000000000000000000000" }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, ibanStatus);
    }

    [Fact]
    public async Task Portal_draft_is_not_issued_until_sms_signature()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var pending = await IssuedDraftAsync(admin, jobId);

        Assert.Equal(InvoiceStatus.Pending, pending.Status);
        Assert.False(pending.IsOfficial);
        Assert.True(pending.RequiresSmsSignature);
        Assert.Null(pending.IssuedAt);
        Assert.Null(pending.Email);
        Assert.NotNull(pending.EArchiveReference);

        // Portala giden taslak: tevkifat faturası, satır vergisi 9015 ve not alanında yazıyla tutar + IBAN.
        var sent = factory.Portal.Created.Last();
        Assert.Equal("TEVKIFAT", sent["faturaTipi"]!.ToString());
        Assert.Equal("1234567890", sent["vknTckn"]!.ToString());
        Assert.Equal("", sent["eposta"]!.ToString());
        Assert.Equal("4800.00", sent["vergilerDahilToplamTutar"]!.ToString());
        Assert.Equal("4640.00", sent["odenecekTutar"]!.ToString());
        var row = sent["malHizmetTable"]![0]!;
        Assert.Equal(624, row["tevkifatKodu"]!.GetValue<int>());
        Assert.Equal("160.00", row["V9015Tutari"]!.ToString());
        Assert.Contains("YALNIZ: DÖRT BİN ALTI YÜZ KIRK TÜRK LİRASI", sent["not"]!.ToString());
        Assert.Contains("TR33 0006 1005 1978 6457 8413 26", sent["not"]!.ToString());

        // Yanlış kod: fatura kesilmez, beklemede kalır.
        var sms = await _client.OkAsync<EArchiveSmsChallengeDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/sms", token: admin);
        Assert.Equal("*** *** 45 67", sms.MaskedPhone);
        var (wrong, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/sign",
            new SignInvoiceRequest { OperationId = sms.OperationId, Code = "000000" }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, wrong);
        var still = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{pending.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Pending, still.Status);

        // Aynı SMS ile doğru kod: GİB "Onaylandı" gösterince fatura kesilir ve e-posta kuyruğa alınır.
        var issued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/sign",
            new SignInvoiceRequest { OperationId = sms.OperationId, Code = FakeGibPortal.SmsCode }, admin);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.True(issued.IsOfficial);
        Assert.StartsWith("GIB2026", issued.EArchiveInvoiceNumber);
        Assert.NotNull(issued.Email);
        Assert.False(issued.RequiresSmsSignature);

        // PDF: GİB PDF vermediği için portaldaki imzalı belge (HTML) PDF'e çevrilir ve saklanır.
        var pdf = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{pending.Id}/pdf") { Headers = { { "Authorization", $"Bearer {admin}" } } });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("Fatura-GIB2026", pdf.Content.Headers.ContentDisposition!.FileNameStar ?? pdf.Content.Headers.ContentDisposition.FileName);
        Assert.Contains($"e-Arşiv fatura {issued.EArchiveReference}", await pdf.Content.ReadAsStringAsync());
        var view = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{pending.Id}/earchive/view") { Headers = { { "Authorization", $"Bearer {admin}" } } });
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains(issued.EArchiveReference!, await view.Content.ReadAsStringAsync());

        Assert.Equal(0, factory.Portal.ActiveSessions);
    }

    [Fact]
    public async Task Single_line_invoice_sends_one_freight_row_but_keeps_jobs()
    {
        var (admin, firstJobId) = await CompletedJobAsync();
        var first = await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{firstJobId}", token: admin);
        var second = await _client.CreateJobAsync(admin, first.Job.CustomerId, assignToMe: true);
        await _client.RunToCompletionAsync(second.Job.Id, admin);

        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [firstJobId, second.Job.Id], NoWithholding = true, SingleLine = true }, admin);
        Assert.True(invoice.SingleLine);
        // İşler kalem olarak saklanır (iş dökümü bunlardan üretilir).
        Assert.Equal(2, invoice.Items.Count);

        var pending = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Pending, pending.Status);

        // Portala tek satır gider: "Nakliye Bedeli", tutar faturanın ara toplamı.
        var rows = factory.Portal.Created.Last()["malHizmetTable"]!.AsArray();
        var row = Assert.Single(rows)!;
        Assert.Equal("Nakliye Bedeli", row["malHizmet"]!.ToString());
        Assert.Equal(invoice.SubTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), row["fiyat"]!.ToString());
        Assert.Equal(invoice.VatAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), row["kdvTutari"]!.ToString());
    }

    [Fact]
    public async Task Portal_draft_can_be_discarded_and_invoice_becomes_editable()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var pending = await IssuedDraftAsync(admin, jobId, withholding: null);
        Assert.Equal("SATIS", factory.Portal.Created.Last()["faturaTipi"]!.ToString());
        Assert.Contains(factory.Portal.Drafts, d => d["ettn"]!.ToString() == pending.EArchiveReference);

        var draft = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/discard", token: admin);
        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Null(draft.EArchiveReference);
        Assert.DoesNotContain(factory.Portal.Drafts, d => d["ettn"]!.ToString() == pending.EArchiveReference);

        // Yeniden gönderilebilir.
        var again = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Pending, again.Status);
    }

    [Fact]
    public async Task Customer_without_tax_number_cannot_be_sent_to_portal()
    {
        var (admin, jobId) = await CompletedJobAsync(taxNumber: null);
        var created = factory.Portal.Created.Count;
        var result = await IssuedDraftAsync(admin, jobId);
        Assert.Equal(InvoiceStatus.Failed, result.Status);
        Assert.Contains("VKN", result.LastError);
        Assert.Equal(created, factory.Portal.Created.Count);
    }

    [Fact]
    public async Task Sync_marks_invoice_issued_when_signed_on_portal()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var pending = await IssuedDraftAsync(admin, jobId);
        factory.Portal.Drafts.Single(d => d["ettn"]!.ToString() == pending.EArchiveReference)["onayDurumu"] = "Onaylandı";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Invoices.Where(i => i.Id == pending.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddMinutes(-5)));
            await scope.ServiceProvider.GetRequiredService<IInvoiceService>().SyncPendingAsync(CancellationToken.None);
        }

        var detail = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{pending.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Issued, detail.Status);
    }

    [Fact]
    public async Task Interrupted_send_is_failed_by_sync_and_can_be_resent()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId], NoWithholding = true }, admin);

        // API gönderim sırasında durdu: fatura Pending, sağlayıcı referansı yok.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Invoices.Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, InvoiceStatus.Pending)
                .SetProperty(i => i.EArchiveProvider, "GibPortal")
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        // Tekrar gönderim anlaşılır bir mesajla reddedilir.
        var again = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue") { Headers = { Authorization = new("Bearer", admin) } });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("zaten e-Arşiv'e gönderiliyor", await again.Content.ReadAsStringAsync());

        // Eşik dolmadan senkronizasyon dokunmaz.
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IInvoiceService>().SyncPendingAsync(CancellationToken.None);
        Assert.Equal(InvoiceStatus.Pending, (await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin)).Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Invoices.Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddMinutes(-10)));
            await scope.ServiceProvider.GetRequiredService<IInvoiceService>().SyncPendingAsync(CancellationToken.None);
        }

        var failed = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Failed, failed.Status);
        Assert.Contains("yarıda kaldı", failed.LastError);
        Assert.Null(failed.IssuedAt);

        var resent = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Pending, resent.Status);
        Assert.NotNull(resent.EArchiveReference);
    }

    [Fact]
    public async Task Portal_timeout_marks_invoice_failed_instead_of_pending()
    {
        var (admin, jobId) = await CompletedJobAsync();
        factory.Portal.SimulateTimeout = true;
        try
        {
            var result = await IssuedDraftAsync(admin, jobId, withholding: null);
            Assert.Equal(InvoiceStatus.Failed, result.Status);
            Assert.Contains("zaman aşımı", result.LastError);
            Assert.Null(result.EArchiveReference);
        }
        finally
        {
            factory.Portal.SimulateTimeout = false;
        }
    }

    [Fact]
    public async Task Portal_requests_carry_browser_headers()
    {
        var (admin, jobId) = await CompletedJobAsync();
        await IssuedDraftAsync(admin, jobId, withholding: null);
        Assert.Contains("Mozilla/5.0", factory.Portal.LastUserAgent);
    }

    private async Task ProcessOutboxAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<InvoiceDetailDto> SignedInvoiceAsync(string admin, Guid jobId)
    {
        var pending = await IssuedDraftAsync(admin, jobId);
        var sms = await _client.OkAsync<EArchiveSmsChallengeDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/sms", token: admin);
        return await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{pending.Id}/earchive/sign",
            new SignInvoiceRequest { OperationId = sms.OperationId, Code = FakeGibPortal.SmsCode }, admin);
    }

    [Fact]
    public async Task Signed_invoice_email_carries_gib_document_as_pdf()
    {
        factory.HtmlPdf.Available = true;
        var (admin, jobId) = await CompletedJobAsync();
        var issued = await SignedInvoiceAsync(admin, jobId);
        await ProcessOutboxAsync();

        var mail = Assert.Single(factory.Email.Sent, m => m.Attachments.Any(a => a.FileName.Contains(issued.EArchiveInvoiceNumber!)));
        var files = mail.Attachments.Where(a => a.ContentId is null).Select(a => a.FileName).ToList();
        Assert.Equal([$"e-Arsiv_Fatura_{issued.EArchiveInvoiceNumber}.pdf", $"Is_Dokumu_{issued.InvoiceNumber}.pdf"], files);
        var gibPdf = System.Text.Encoding.UTF8.GetString(mail.Attachments.First(a => a.FileName.StartsWith("e-Arsiv_Fatura_")).Content);
        Assert.StartsWith("%PDF", gibPdf);
        Assert.Contains($"e-Arşiv fatura {issued.EArchiveReference}", gibPdf);
        // AKNA logosu belgenin önüne eklenir; GİB içeriği aynen kalır.
        Assert.Contains("<svg", gibPdf);
        Assert.True(gibPdf.IndexOf("<svg", StringComparison.Ordinal) < gibPdf.IndexOf("e-Arşiv fatura", StringComparison.Ordinal));
        // Tarayıcıda logo karekodun altına taşınır (karekod yoksa üstte kalır).
        Assert.Contains("getElementById('qrcode')", gibPdf);
    }

    [Fact]
    public async Task Without_browser_email_carries_own_pdf_and_gib_html()
    {
        factory.HtmlPdf.Available = false;
        try
        {
            var (admin, jobId) = await CompletedJobAsync();
            var issued = await SignedInvoiceAsync(admin, jobId);
            await ProcessOutboxAsync();

            var mail = Assert.Single(factory.Email.Sent, m => m.Attachments.Any(a => a.FileName.Contains(issued.EArchiveInvoiceNumber!)));
            var files = mail.Attachments.Where(a => a.ContentId is null).Select(a => a.FileName).ToList();
            Assert.Equal([$"e-Arsiv_Fatura_{issued.EArchiveInvoiceNumber}.html", $"Fatura-{issued.EArchiveInvoiceNumber}.pdf", $"Is_Dokumu_{issued.InvoiceNumber}.pdf"], files);
        }
        finally
        {
            factory.HtmlPdf.Available = true;
        }
    }

    [Fact]
    public async Task Portal_draft_pdf_comes_from_gib_with_logo()
    {
        var (admin, jobId) = await CompletedJobAsync();
        var pending = await IssuedDraftAsync(admin, jobId);
        Assert.Equal(InvoiceStatus.Pending, pending.Status);

        var pdf = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{pending.Id}/pdf") { Headers = { { "Authorization", $"Bearer {admin}" } } });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("TASLAK-", pdf.Content.Headers.ContentDisposition!.FileNameStar ?? pdf.Content.Headers.ContentDisposition.FileName);
        var content = await pdf.Content.ReadAsStringAsync();
        Assert.Contains($"e-Arşiv fatura {pending.EArchiveReference}", content);
        Assert.Contains("<svg", content);

        // Portal taslağı saklanmaz; fatura hâlâ beklemede.
        var still = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{pending.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Pending, still.Status);
    }
}
