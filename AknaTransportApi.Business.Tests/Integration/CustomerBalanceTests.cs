using System.Net;

namespace AknaTransportApi.Business.Tests.Integration;

public class CustomerBalanceTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Issued_invoice_adds_to_balance_and_collections_reduce_it()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);

        var draft = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);
        var before = await _client.OkAsync<CustomerLedgerDto>(HttpMethod.Get, $"/api/customers/{customer.Id}/ledger", token: admin);
        Assert.Equal(0, before.Balance.Balance); // taslak fatura bakiyeye yazılmaz

        var issued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{draft.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        var afterIssue = await _client.OkAsync<CustomerLedgerDto>(HttpMethod.Get, $"/api/customers/{customer.Id}/ledger", token: admin);
        Assert.Equal(issued.PayableAmount, afterIssue.Balance.Invoiced);
        Assert.Equal(issued.PayableAmount, afterIssue.Balance.Balance);
        Assert.Single(afterIssue.Entries);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var collected = await _client.OkAsync<CustomerTransactionDto>(HttpMethod.Post, $"/api/customers/{customer.Id}/transactions",
            new CustomerTransactionRequest(CustomerTransactionType.Collection, 100m, today, "Havale/EFT", "Kısmi ödeme"), admin);
        await _client.OkAsync<CustomerTransactionDto>(HttpMethod.Post, $"/api/customers/{customer.Id}/transactions",
            new CustomerTransactionRequest(CustomerTransactionType.Refund, 10m, today, null, null), admin);

        var ledger = await _client.OkAsync<CustomerLedgerDto>(HttpMethod.Get, $"/api/customers/{customer.Id}/ledger", token: admin);
        Assert.Equal(issued.PayableAmount - 100m + 10m, ledger.Balance.Balance);
        Assert.Equal(100m, ledger.Balance.Collected);
        Assert.Equal(10m, ledger.Balance.Refunded);
        Assert.Equal(3, ledger.Entries.Count);
        Assert.Equal(ledger.Balance.Balance, ledger.Entries[0].Balance); // en yeni satırın bakiyesi = güncel bakiye

        var overview = await _client.OkAsync<CustomerBalanceOverviewDto>(HttpMethod.Get, "/api/customers/balances?openOnly=true", token: admin);
        Assert.Contains(overview.Items, i => i.CustomerId == customer.Id && i.Balance == ledger.Balance.Balance);

        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/customers/{customer.Id}/transactions/{collected.Id}", token: admin);
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{draft.Id}/cancel", new CancelInvoiceRequest { Reason = "Hatalı" }, admin);
        var afterCancel = await _client.OkAsync<CustomerLedgerDto>(HttpMethod.Get, $"/api/customers/{customer.Id}/ledger", token: admin);
        Assert.Equal(10m, afterCancel.Balance.Balance); // iptal edilen fatura bakiyeden düşer
    }

    [Fact]
    public async Task Invalid_transaction_is_rejected_and_driver_cannot_see_balances()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var (status, _) = await _client.SendAsync<object>(HttpMethod.Post, $"/api/customers/{customer.Id}/transactions",
            new CustomerTransactionRequest(CustomerTransactionType.Collection, 0m, DateOnly.FromDateTime(DateTime.UtcNow), null, null), admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);

        var driver = await _client.CreateDriverAsync(admin);
        var (forbidden, _) = await _client.SendAsync<object>(HttpMethod.Get, "/api/customers/balances", token: driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
    }
}
