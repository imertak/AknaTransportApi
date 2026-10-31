using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken ct);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct);
    Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(Guid id, CustomerRequest request, CancellationToken ct);
    Task<CustomerDeleteResult> DeleteAsync(Guid id, CancellationToken ct);
    Task<CustomerSummaryDto> GetSummaryAsync(Guid id, CancellationToken ct);
    Task EnsureExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Kaydı yalnızca unit of work'e ekler (iş oluştururken yeni müşteri için).</summary>
    Customer AddWithoutSaving(CustomerRequest request);
}
