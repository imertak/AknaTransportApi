using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

[Route("api/customers")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class CustomersController(ICustomerService customerService, ICustomerAccountService accounts, IJobService jobService, IInvoiceService invoiceService) : ApiControllerBase
{
    /// <summary>Müşterileri listeler. Varsayılan olarak yalnızca aktif müşteriler; tümü için ?isActive= (boş).</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<CustomerDto>>>> List([FromQuery] CustomerQuery query, CancellationToken ct) =>
        Success(await customerService.ListAsync(query, ct));

    /// <summary>
    /// Müşteri bakiyeleri: kesilen fatura, tahsilat (gelen), iade (giden), borç kaydı ve kalan bakiye.
    /// Borcu en yüksek müşteri önce gelir. openOnly=true yalnızca bakiyesi olanlar.
    /// </summary>
    [HttpGet("balances")]
    public async Task<ActionResult<BaseResponse<CustomerBalanceOverviewDto>>> Balances([FromQuery] CustomerBalanceQuery query, CancellationToken ct) =>
        Success(await accounts.GetBalancesAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<CustomerDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await customerService.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<BaseResponse<CustomerDto>>> Create(CustomerRequest request, CancellationToken ct)
    {
        var customer = await customerService.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = customer.Id }, customer, "Müşteri oluşturuldu.");
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BaseResponse<CustomerDto>>> Update(Guid id, CustomerRequest request, CancellationToken ct) =>
        Success(await customerService.UpdateAsync(id, request, ct), "Müşteri güncellendi.");

    /// <summary>Geçmiş iş veya faturası olan müşteri silinmez, pasife alınır.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<BaseResponse<CustomerDeleteResult>>> Delete(Guid id, CancellationToken ct)
    {
        var result = await customerService.DeleteAsync(id, ct);
        return Success(result, result.Message);
    }

    [HttpGet("{id:guid}/jobs")]
    public async Task<ActionResult<BaseResponse<PagedResult<JobDto>>>> Jobs(Guid id, [FromQuery] JobQuery query, CancellationToken ct)
    {
        await customerService.EnsureExistsAsync(id, ct);
        query.CustomerId = id;
        return Success(await jobService.ListAsync(query, ct));
    }

    [HttpGet("{id:guid}/invoices")]
    public async Task<ActionResult<BaseResponse<PagedResult<InvoiceListItemDto>>>> Invoices(Guid id, [FromQuery] InvoiceQuery query, CancellationToken ct)
    {
        await customerService.EnsureExistsAsync(id, ct);
        query.CustomerId = id;
        return Success(await invoiceService.ListAsync(query, ct));
    }

    /// <summary>
    /// Müşterinin geçmiş işlerinde kullanılan yükleme/teslimat adresleri ve kayıtlı koordinatları.
    /// Filtreler: stopType=Pickup|Delivery, search, limit (en fazla 100).
    /// </summary>
    [HttpGet("{id:guid}/addresses")]
    public async Task<ActionResult<BaseResponse<IReadOnlyList<CustomerAddressDto>>>> Addresses(Guid id, [FromQuery] CustomerAddressQuery query, CancellationToken ct) =>
        Success(await customerService.GetAddressesAsync(id, query, ct));

    /// <summary>Toplam / tamamlanan iş, toplam nakliye ve fatura tutarı, son işler ve fatura geçmişi.</summary>
    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<BaseResponse<CustomerSummaryDto>>> Summary(Guid id, CancellationToken ct) =>
        Success(await customerService.GetSummaryAsync(id, ct));

    /// <summary>Cari hesap ekstresi: kesilen faturalar ve elle girilen hareketler, en yenisi önce, satır sonrası bakiyeyle.</summary>
    [HttpGet("{id:guid}/ledger")]
    public async Task<ActionResult<BaseResponse<CustomerLedgerDto>>> Ledger(Guid id, CancellationToken ct) =>
        Success(await accounts.GetLedgerAsync(id, ct));

    /// <summary>Tahsilat (Collection), müşteriye ödeme/iade (Refund) veya faturasız borç (Charge) ekler.</summary>
    [HttpPost("{id:guid}/transactions")]
    public async Task<ActionResult<BaseResponse<CustomerTransactionDto>>> AddTransaction(Guid id, CustomerTransactionRequest request, CancellationToken ct) =>
        Success(await accounts.AddTransactionAsync(id, request, ct), "Hareket kaydedildi.");

    [HttpPut("{id:guid}/transactions/{transactionId:guid}")]
    public async Task<ActionResult<BaseResponse<CustomerTransactionDto>>> UpdateTransaction(Guid id, Guid transactionId, CustomerTransactionRequest request, CancellationToken ct) =>
        Success(await accounts.UpdateTransactionAsync(id, transactionId, request, ct), "Hareket güncellendi.");

    [HttpDelete("{id:guid}/transactions/{transactionId:guid}")]
    public async Task<ActionResult<BaseResponse>> DeleteTransaction(Guid id, Guid transactionId, CancellationToken ct)
    {
        await accounts.DeleteTransactionAsync(id, transactionId, ct);
        return Done("Hareket silindi.");
    }
}
