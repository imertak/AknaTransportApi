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

        // PDF: GİB PDF vermediği için e-Arşiv bilgileriyle üretilir; HTML görüntü portaldan alınır.
        var pdf = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{pending.Id}/pdf") { Headers = { { "Authorization", $"Bearer {admin}" } } });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.StartsWith("Fatura-GIB2026", pdf.Content.Headers.ContentDisposition!.FileNameStar ?? pdf.Content.Headers.ContentDisposition.FileName);
        var view = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{pending.Id}/earchive/view") { Headers = { { "Authorization", $"Bearer {admin}" } } });
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains(issued.EArchiveReference!, await view.Content.ReadAsStringAsync());

        Assert.Equal(0, factory.Portal.ActiveSessions);
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
}
