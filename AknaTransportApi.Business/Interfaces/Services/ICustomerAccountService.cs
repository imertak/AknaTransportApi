namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>Müşteri cari hesabı: bakiye, ekstre ve elle girilen tahsilat/iade/borç hareketleri.</summary>
public interface ICustomerAccountService
{
    Task<CustomerBalanceOverviewDto> GetBalancesAsync(CustomerBalanceQuery query, CancellationToken ct);
    Task<CustomerLedgerDto> GetLedgerAsync(Guid customerId, CancellationToken ct);
    Task<CustomerTransactionDto> AddTransactionAsync(Guid customerId, CustomerTransactionRequest request, CancellationToken ct);
    Task<CustomerTransactionDto> UpdateTransactionAsync(Guid customerId, Guid transactionId, CustomerTransactionRequest request, CancellationToken ct);
    Task DeleteTransactionAsync(Guid customerId, Guid transactionId, CancellationToken ct);
}
