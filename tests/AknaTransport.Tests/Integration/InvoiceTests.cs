using AknaTransport.Business.Common;
using System.Net;
using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Features.Outbox;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Infrastructure.Persistence;
using AknaTransport.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransport.Tests.Integration;

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
    public async Task Uncompleted_job_cannot_be_invoiced()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id);
        var (status, _) = await _client.SendAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
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
        Assert.Equal(2, mail.Attachments.Count);
        Assert.All(mail.Attachments, a => Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(a.Content, 0, 4)));

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
