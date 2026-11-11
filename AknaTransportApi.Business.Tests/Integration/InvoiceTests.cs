using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Integration;

public class InvoiceTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<(string Admin, Guid CustomerId, Guid JobId)> CompletedJobAsync()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        return (admin, customer.Id, job.Job.Id);
    }

    private async Task<int> ProcessOutboxAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync(CancellationToken.None);
    }

    private async Task MakeOutboxDueAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.OutboxMessages.Where(m => m.Status == OutboxMessageStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1)));
    }

    private async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Same_job_cannot_be_added_to_two_invoices()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.False(invoice.IsOfficial);
        Assert.Equal(4800m, invoice.GrandTotal); // 4000 + %20 KDV

        var (status, body) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(body!.IsSuccess);

        // Taslak iptal edilince iş yeniden faturalanabilir.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/cancel", new CancelInvoiceRequest { Reason = "Hatalı" }, admin);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
    }

    [Fact]
    public async Task Only_cancelled_invoice_can_be_deleted()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);

        var (draftStatus, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/invoices/{invoice.Id}", null, admin);
        Assert.Equal(HttpStatusCode.Conflict, draftStatus);

        // Taslak e-postayla gönderilmiş olsun: e-posta kaydı silmeyi engellememeli.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/send-email", null, admin);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/cancel", new CancelInvoiceRequest { Reason = "Hatalı" }, admin);
        var (status, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/invoices/{invoice.Id}", null, admin);
        Assert.Equal(HttpStatusCode.OK, status);

        var (getStatus, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", null, admin);
        Assert.Equal(HttpStatusCode.NotFound, getStatus);
        Assert.False(await QueryAsync(db => db.Set<InvoiceItem>().AnyAsync(i => i.InvoiceId == invoice.Id)));
        Assert.False(await QueryAsync(db => db.Set<EmailLog>().AnyAsync(e => e.InvoiceId == invoice.Id)));
        // İş yeniden faturalanabilir.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
    }

    [Fact]
    public async Task Admin_can_invoice_unfinished_job_but_not_cancelled_one()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);

        // Henüz kabul edilmemiş (atanmış) iş faturalanabilir.
        var open = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [open.Job.Id] }, admin);
        Assert.Equal(4800m, invoice.GrandTotal);

        // Faturalanan iş yürümeye devam eder ve tamamlanabilir; ancak iptal edilemez.
        var (cancelInvoiced, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{open.Job.Id}/cancel", new CancelJobRequest { Reason = "Test" }, admin);
        Assert.NotEqual(HttpStatusCode.OK, cancelInvoiced);
        var done = await _client.RunToCompletionAsync(open.Job.Id, admin);
        Assert.Equal(JobStatus.Completed, done.Job.Status);

        // İptal edilmiş iş faturalanamaz.
        var cancelled = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{cancelled.Job.Id}/cancel", new CancelJobRequest { Reason = "Test" }, admin);
        var (status, body) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [cancelled.Job.Id] }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(body!.Errors!, e => e.Message.Contains("iptal edildiği için faturalanamaz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invoice_list_includes_job_and_description_for_mobile_list()
    {
        var (admin, customerId, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices",
            new CreateInvoiceRequest { JobIds = [jobId], Notes = "Ekim taşımaları" }, admin);

        var page = await _client.OkAsync<PagedResult<InvoiceListItemDto>>(HttpMethod.Get, $"/api/invoices?customerId={customerId}", null, admin);
        var item = Assert.Single(page.Items, i => i.Id == invoice.Id);
        Assert.Equal(jobId, item.FirstJobId);
        Assert.Equal(invoice.Items[0].JobNumber, item.FirstJobNumber);
        Assert.Equal("Ekim taşımaları", item.Description);
        Assert.Equal(invoice.CustomerEmail, item.CustomerEmail);
    }

    [Fact]
    public async Task Invoice_line_has_route_but_not_job_extra_description()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", new CreateJobRequest
        {
            CustomerId = customer.Id, JobDate = new DateOnly(2026, 10, 21), StoreName = "Skyland", VehicleType = VehicleType.Panelvan,
            Stops = [new() { StopType = StopType.Pickup, Address = "Skyland AVM" }, new() { StopType = StopType.Delivery, Address = "Manço Merkez Depo" }],
            Description = "Gece yükleme gündüz boşaltma", Price = 4000,
        }, admin);

        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);
        var line = Assert.Single(invoice.Items).Description;
        Assert.Contains("Skyland AVM - Manço Merkez Depo", line);
        Assert.DoesNotContain("Gece yükleme", line);
        Assert.Null(invoice.Notes);
    }

    [Fact]
    public async Task Customer_with_multiple_emails_gets_invoice_email_with_cc()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Post, "/api/customers", new CustomerRequest
        {
            CompanyName = $"Çoklu {Guid.NewGuid():N}"[..16],
            Emails = ["muhasebe@coklu.example", " satinalma@coklu.example ", "", "MUHASEBE@coklu.example", "depo@coklu.example"],
        }, admin);
        Assert.Equal(["muhasebe@coklu.example", "satinalma@coklu.example", "depo@coklu.example"], customer.Emails);
        Assert.Equal("muhasebe@coklu.example", customer.Email);

        // Eski istemci yalnızca Email gönderirse ek adresler korunur.
        var updated = await _client.OkAsync<CustomerDto>(HttpMethod.Put, $"/api/customers/{customer.Id}",
            new CustomerRequest { CompanyName = customer.CompanyName, Email = "yeni@coklu.example" }, admin);
        Assert.Equal(["yeni@coklu.example", "satinalma@coklu.example", "depo@coklu.example"], updated.Emails);

        var (status, body) = await _client.SendAsync<CustomerDto>(HttpMethod.Put, $"/api/customers/{customer.Id}",
            new CustomerRequest { CompanyName = customer.CompanyName, Emails = ["yeni@coklu.example", "gecersiz-adres"] }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(body!.Errors!, e => e.Message.Contains("gecersiz-adres"));

        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        await ProcessOutboxAsync();

        var mail = Assert.Single(factory.Email.Sent, m => m.To == "yeni@coklu.example");
        Assert.Equal(["satinalma@coklu.example", "depo@coklu.example"], mail.Cc);
    }

    [Fact]
    public async Task Draft_invoice_can_be_emailed_without_being_issued()
    {
        var admin = await _client.AdminTokenAsync();
        var email = $"taslak{Guid.NewGuid():N}"[..14] + "@example.com";
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Post, "/api/customers",
            new CustomerRequest { CompanyName = $"Taslak {Guid.NewGuid():N}"[..16], Emails = [email, "cc@taslak.example"] }, admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);

        var queued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/send-email", token: admin);
        Assert.Equal(EmailStatus.Pending, queued.ManualEmail!.Status);
        // Kuyrukta bekleyen gönderim varken ikinci istek reddedilir (çift tıklama).
        var (dupStatus, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/send-email", null, admin);
        Assert.Equal(HttpStatusCode.Conflict, dupStatus);

        await ProcessOutboxAsync();
        var mail = Assert.Single(factory.Email.Sent, m => m.To == email);
        Assert.Contains("Taslağı", mail.Subject);
        Assert.Contains("TASLAK", mail.HtmlBody);
        Assert.Equal(["cc@taslak.example"], mail.Cc);
        Assert.Equal([$"Fatura_Taslagi_{invoice.InvoiceNumber}.pdf", $"Is_Dokumu_{invoice.InvoiceNumber}.pdf"], mail.Attachments.Where(a => a.ContentId is null).Select(a => a.FileName));
        // Başlıktaki logo satır içi görsel olarak eklenir.
        Assert.Contains(mail.Attachments, a => a.ContentId == "akna-logo" && a.ContentType == "image/png");
        Assert.Contains("cid:akna-logo", mail.HtmlBody);

        // Fatura taslak kalır; tekrar gönderilebilir.
        var after = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Draft, after.Status);
        Assert.Equal(EmailStatus.Sent, after.ManualEmail!.Status);
        Assert.Null(after.Email);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/send-email", token: admin);
        await ProcessOutboxAsync();
        Assert.Equal(2, factory.Email.Sent.Count(m => m.To == email));

        // Kesildiğinde otomatik e-posta elle gönderimlerden bağımsız olarak gider.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        await ProcessOutboxAsync();
        Assert.Contains(factory.Email.Sent, m => m.To == email && m.Attachments.Any(a => a.FileName.StartsWith("e-Arsiv_Fatura_")));
    }

    [Fact]
    public async Task Free_form_invoice_for_registered_customer_has_manual_lines_and_no_job_report()
    {
        var admin = await _client.AdminTokenAsync();
        var email = $"serbest{Guid.NewGuid():N}"[..14] + "@example.com";
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Post, "/api/customers",
            new CustomerRequest { CompanyName = $"Serbest {Guid.NewGuid():N}"[..16], Email = email, TaxNumber = "1234567890" }, admin);

        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest
        {
            CustomerId = customer.Id,
            NoWithholding = true,
            Lines = [new InvoiceLineRequest { Description = "Depolama hizmeti", Quantity = 2, UnitPrice = 1500m }, new InvoiceLineRequest { Description = "Forklift", UnitPrice = 1000m }],
        }, admin);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.True(invoice.IsFreeForm);
        Assert.Equal(customer.Id, invoice.CustomerId);
        Assert.Equal(4000m, invoice.SubTotal);
        Assert.Equal(4800m, invoice.GrandTotal);
        Assert.Equal(2, invoice.Items.Count);
        Assert.All(invoice.Items, i => Assert.Null(i.JobId));
        Assert.Contains(invoice.Items, i => i.Description == "Depolama hizmeti" && i.Quantity == 2 && i.LineTotal == 3000m);

        var list = await _client.OkAsync<PagedResult<InvoiceListItemDto>>(HttpMethod.Get, $"/api/invoices?customerId={customer.Id}", token: admin);
        var row = Assert.Single(list.Items);
        Assert.Equal(0, row.JobCount);
        Assert.Null(row.FirstJobId);

        // Kalemler düzenlenebilir; serbest faturaya iş eklenemez.
        var updated = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Put, $"/api/invoices/{invoice.Id}", new UpdateInvoiceRequest
        {
            InvoiceDate = invoice.InvoiceDate,
            VatRate = 20,
            Lines = [new InvoiceLineRequest { Description = "Depolama hizmeti", Quantity = 1, UnitPrice = 2500m }],
        }, admin);
        Assert.Equal(2500m, updated.SubTotal);
        Assert.Single(updated.Items);

        // Taslak e-postası: iş dökümü eklenmez, konu dökümden söz etmez.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/send-email", token: admin);
        await ProcessOutboxAsync();
        var draftMail = Assert.Single(factory.Email.Sent, m => m.To == email);
        Assert.DoesNotContain("Döküm", draftMail.Subject);
        Assert.Equal([$"Fatura_Taslagi_{invoice.InvoiceNumber}.pdf"], draftMail.Attachments.Where(a => a.ContentId is null).Select(a => a.FileName));
        Assert.DoesNotContain("İş sayısı", draftMail.TextBody);

        // Kesilir ve kesilen faturanın e-postası da yalnızca faturayı içerir.
        var issued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        await ProcessOutboxAsync();
        var issuedMail = Assert.Single(factory.Email.Sent, m => m.To == email && m.Subject == EmailTemplates.FreeFormInvoiceSubject);
        Assert.DoesNotContain(issuedMail.Attachments, a => a.FileName.StartsWith("Is_Dokumu_"));
    }

    [Fact]
    public async Task Free_form_invoice_creates_unregistered_customer()
    {
        var admin = await _client.AdminTokenAsync();
        var taxNumber = string.Concat(Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("0000000000"))[..10];
        var name = $"Yeni Alıcı {Guid.NewGuid():N}"[..20];
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest
        {
            NewCustomer = new CustomerRequest { CompanyName = name, TaxNumber = taxNumber, TaxOffice = "Gebze", Address = "Gebze OSB", Email = "yeni@alici.example" },
            Lines = [new InvoiceLineRequest { Description = "Nakliye", UnitPrice = 1000m }],
        }, admin);
        Assert.Equal(name, invoice.CustomerName);
        var customer = await _client.OkAsync<CustomerDto>(HttpMethod.Get, $"/api/customers/{invoice.CustomerId}", token: admin);
        Assert.Equal(taxNumber, customer.TaxNumber);
        Assert.Equal("Gebze OSB", customer.Address);

        // Aynı VKN ile ikinci kez "yeni müşteri" girilemez; kayıtlı müşteri seçilmelidir.
        var (dupStatus, dup) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest
        {
            NewCustomer = new CustomerRequest { CompanyName = "Başka", TaxNumber = taxNumber, Email = "baska@alici.example" },
            Lines = [new InvoiceLineRequest { Description = "Nakliye", UnitPrice = 1000m }],
        }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, dupStatus);
        Assert.Contains(name, dup!.Message);

        // Yeni müşteride VKN/TCKN ve e-posta zorunludur; müşteri veya kalem olmadan fatura oluşmaz.
        var (missingStatus, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest
        {
            NewCustomer = new CustomerRequest { CompanyName = "Eksik" },
            Lines = [new InvoiceLineRequest { Description = "Nakliye", UnitPrice = 1000m }],
        }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, missingStatus);
        var (noCustomer, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest
        {
            Lines = [new InvoiceLineRequest { Description = "Nakliye", UnitPrice = 1000m }],
        }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, noCustomer);
        var (noLines, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { CustomerId = invoice.CustomerId }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, noLines);
    }

    [Fact]
    public async Task Invoice_requires_valid_customer_email()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin, email: null);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        var (status, body) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("e-posta", body!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejected_provider_response_does_not_issue_invoice()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);

        factory.EArchive.Mode = FakeEArchiveMode.Reject;
        try
        {
            var result = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
            Assert.Equal(InvoiceStatus.Failed, result.Status);
            Assert.Null(result.IssuedAt);
            Assert.Null(result.Email);

            factory.EArchive.Mode = FakeEArchiveMode.Throw;
            var retry = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
            Assert.Equal(InvoiceStatus.Failed, retry.Status);
        }
        finally
        {
            factory.EArchive.Mode = FakeEArchiveMode.Issue;
        }

        var hasOutbox = await QueryAsync(db => db.EmailLogs.AnyAsync(e => e.InvoiceId == invoice.Id));
        Assert.False(hasOutbox);
    }

    [Fact]
    public async Task Successful_issue_queues_email_and_sends_both_pdfs_once()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
        var sendsBefore = factory.EArchive.SendCount;

        var issued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.True(issued.IsOfficial);
        Assert.Equal(sendsBefore + 1, factory.EArchive.SendCount);
        Assert.NotNull(issued.Email);
        Assert.Equal(EmailStatus.Pending, issued.Email!.Status);

        var outbox = await QueryAsync(db => db.OutboxMessages.CountAsync(m => m.Type == OutboxMessageTypes.InvoiceEmail && m.Payload.Contains(invoice.Id.ToString())));
        Assert.Equal(1, outbox);

        // Aynı fatura ikinci kez kesilemez.
        var (again, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(HttpStatusCode.Conflict, again);

        await ProcessOutboxAsync();
        await ProcessOutboxAsync();

        var mails = factory.Email.Sent.Where(m => m.Subject == EmailTemplates.InvoiceSubject && m.To == "muhasebe@musteri.example")
            .Where(m => m.Attachments.Any(a => a.FileName.Contains("TST2026000000001"))).ToList();
        var detail = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
        Assert.Equal(EmailStatus.Sent, detail.Email!.Status);
        Assert.NotNull(detail.Email.SentAt);

        var mail = Assert.Single(mails.Where(m => m.TextBody.Contains("AKNA Transport")).Take(1));
        var files = mail.Attachments.Where(a => a.ContentId is null).ToList();
        Assert.Equal(2, files.Count);
        Assert.All(files, a => Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(a.Content, 0, 4)));

        // Gönderilmiş e-posta yeniden kuyruğa alınamaz.
        var (resend, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/resend-email", token: admin);
        Assert.Equal(HttpStatusCode.Conflict, resend);
        await ProcessOutboxAsync();
        var log = await QueryAsync(db => db.EmailLogs.SingleAsync(e => e.InvoiceId == invoice.Id));
        Assert.Equal(EmailStatus.Sent, log.Status);
        Assert.Equal(1, log.AttemptCount);
    }

    [Fact]
    public async Task Email_failure_does_not_reissue_invoice_and_can_be_retried()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        var sendsAfterIssue = factory.EArchive.SendCount;

        factory.Email.Fail = true;
        try
        {
            await ProcessOutboxAsync();
            var afterFailure = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
            Assert.Equal(InvoiceStatus.Issued, afterFailure.Status);
            Assert.Equal(EmailStatus.Pending, afterFailure.Email!.Status); // tekrar denenecek
            Assert.NotNull(afterFailure.Email.LastError);
            Assert.Equal(1, afterFailure.Email.AttemptCount);

            var message = await QueryAsync(db => db.OutboxMessages.SingleAsync(m => m.Payload.Contains(invoice.Id.ToString())));
            Assert.True(message.NextAttemptAt > DateTime.UtcNow, "Üstel geri çekilme ile ileri bir zamana ertelenmeli.");
        }
        finally
        {
            factory.Email.Fail = false;
        }

        await MakeOutboxDueAsync();
        await ProcessOutboxAsync();

        var final = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Issued, final.Status);
        Assert.Equal(EmailStatus.Sent, final.Email!.Status);
        Assert.Equal(sendsAfterIssue, factory.EArchive.SendCount); // e-Arşiv'e tekrar gönderilmedi
    }

    [Fact]
    public async Task Issued_invoice_pdf_is_official_document_and_draft_is_marked()
    {
        var (admin, _, jobId) = await CompletedJobAsync();
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);

        var draft = await GetPdfAsync($"/api/invoices/{invoice.Id}/pdf", admin);
        Assert.StartsWith("TASLAK-", draft.FileName);

        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        var official = await GetPdfAsync($"/api/invoices/{invoice.Id}/pdf", admin);
        Assert.StartsWith("Fatura-", official.FileName);
        Assert.Equal(FakeEArchiveAdapter.PdfBytes, official.Bytes);
    }

    private async Task<(string FileName, byte[] Bytes)> GetPdfAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new("Bearer", token);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        return (response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!.Trim('"'),
            await response.Content.ReadAsByteArrayAsync());
    }
    private async Task<(HttpStatusCode Status, BaseResponse<InvoiceDetailDto>? Body)> UploadManualAsync(Guid invoiceId, byte[] content, string? number, string token)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "fatura.pdf");
        if (number is not null) form.Add(new StringContent(number), "invoiceNumber");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/manual-pdf") { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var body = string.IsNullOrWhiteSpace(text) ? null
            : System.Text.Json.JsonSerializer.Deserialize<BaseResponse<InvoiceDetailDto>>(text, AknaTransportApi.Business.Tests.Infrastructure.Api.Json);
        return (response.StatusCode, body);
    }

    [Fact]
    public async Task Manually_uploaded_invoice_is_issued_and_keeps_job_report()
    {
        var (admin, customerId, jobId) = await CompletedJobAsync();
        var draft = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
        var sendsBefore = factory.EArchive.SendCount;

        // PDF olmayan dosya reddedilir.
        var (badStatus, _) = await UploadManualAsync(draft.Id, "merhaba"u8.ToArray(), null, admin);
        Assert.Equal(HttpStatusCode.BadRequest, badStatus);

        var pdf = "%PDF-1.4\n% elle yüklenen fatura\n%%EOF"u8.ToArray();
        var (status, body) = await UploadManualAsync(draft.Id, pdf, "ABC2026000000123", admin);
        Assert.Equal(HttpStatusCode.OK, status);
        var invoice = body!.Data!;
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.True(invoice.IsManual);
        Assert.True(invoice.IsOfficial);
        Assert.Equal("ABC2026000000123", invoice.EArchiveInvoiceNumber);
        Assert.Null(invoice.Email); // otomatik e-posta gönderilmez
        Assert.Equal(sendsBefore, factory.EArchive.SendCount); // e-Arşiv'e gidilmez

        // Fatura PDF'i yüklenen dosyanın kendisidir; iş dökümü üretilmeye devam eder.
        var (pdfStatus, pdfBytes) = await GetBytesAsync($"/api/invoices/{draft.Id}/pdf", admin);
        Assert.Equal(HttpStatusCode.OK, pdfStatus);
        Assert.Equal(pdf, pdfBytes);
        var (reportStatus, _) = await GetBytesAsync($"/api/invoices/{draft.Id}/job-report/pdf", admin);
        Assert.Equal(HttpStatusCode.OK, reportStatus);

        // Bakiyeye kesilmiş fatura olarak yazılır; iş "faturalandı" görünür.
        var ledger = await _client.OkAsync<CustomerLedgerDto>(HttpMethod.Get, $"/api/customers/{customerId}/ledger", token: admin);
        Assert.Equal(invoice.PayableAmount, ledger.Balance.Invoiced);

        // Dosya değiştirilebilir.
        var replacement = "%PDF-1.7\n% yeni\n%%EOF"u8.ToArray();
        var (replaceStatus, replaced) = await UploadManualAsync(draft.Id, replacement, null, admin);
        Assert.Equal(HttpStatusCode.OK, replaceStatus);
        Assert.Equal("ABC2026000000123", replaced!.Data!.EArchiveInvoiceNumber);
        Assert.Equal(replacement, (await GetBytesAsync($"/api/invoices/{draft.Id}/pdf", admin)).Body);

        // İptal e-Arşiv'e gitmeden yapılır; iş yeniden faturalanabilir.
        var cancelled = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{draft.Id}/cancel", new CancelInvoiceRequest { Reason = "Hatalı" }, admin);
        Assert.Equal(InvoiceStatus.Cancelled, cancelled.Status);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [jobId] }, admin);
    }

    private async Task<(HttpStatusCode Status, byte[] Body)> GetBytesAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsByteArrayAsync());
    }

}

public class InvoiceWithoutProviderTests(NoEArchiveApiFactory factory) : IClassFixture<NoEArchiveApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Invoice_is_not_issued_without_real_e_archive_provider()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);

        var (status, body) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("e-Arşiv", body!.Message);

        var detail = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Get, $"/api/invoices/{invoice.Id}", token: admin);
        Assert.Equal(InvoiceStatus.Draft, detail.Status);
        Assert.False(detail.IsOfficial);
        Assert.Null(detail.Email);
        Assert.Equal(0, factory.EArchive.SendCount);
    }

}
