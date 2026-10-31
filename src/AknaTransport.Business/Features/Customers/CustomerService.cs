using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Features.Jobs;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AknaTransport.Business.Features.Customers;

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

public sealed class CustomerService(
    IRepository<Customer> customers,
    IRepository<TransportJob> jobs,
    IRepository<Invoice> invoices,
    IUnitOfWork unitOfWork) : ICustomerService
{
    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken ct)
    {
        var q = customers.QueryAsNoTracking();
        if (query.IsActive is { } active) q = q.Where(c => c.IsActive == active);
        if (query.Search.TrimOrNull() is { } search)
        {
            var term = search.ToLower();
            q = q.Where(c => c.CompanyName.ToLower().Contains(term)
                             || (c.ContactPerson != null && c.ContactPerson.ToLower().Contains(term))
                             || (c.Email != null && c.Email.ToLower().Contains(term))
                             || (c.TaxNumber != null && c.TaxNumber.Contains(term)));
        }

        q = (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("createdat", true) => q.OrderByDescending(c => c.CreatedAt),
            ("createdat", false) => q.OrderBy(c => c.CreatedAt),
            ("companyname", true) => q.OrderByDescending(c => c.CompanyName),
            _ => q.OrderBy(c => c.CompanyName),
        };
        return await q.Select(CustomerMappings.Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) =>
        await customers.QueryAsNoTracking().Where(c => c.Id == id).Select(CustomerMappings.Projection).FirstOrDefaultAsync(ct)
        ?? throw NotFoundException.For("Müşteri");

    public async Task<CustomerDto> CreateAsync(CustomerRequest request, CancellationToken ct)
    {
        var customer = AddWithoutSaving(request);
        await unitOfWork.SaveChangesAsync(ct);
        return customer.ToDto();
    }

    public Customer AddWithoutSaving(CustomerRequest request)
    {
        var customer = new Customer { IsActive = true };
        customer.Apply(request);
        customers.Add(customer);
        return customer;
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, CustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Müşteri");
        customer.Apply(request);
        customer.IsActive = request.IsActive;
        await unitOfWork.SaveChangesAsync(ct);
        return customer.ToDto();
    }

    public async Task<CustomerDeleteResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Müşteri");
        var hasHistory = await jobs.QueryAsNoTracking().AnyAsync(j => j.CustomerId == id, ct)
                         || await invoices.QueryAsNoTracking().AnyAsync(i => i.CustomerId == id, ct);

        if (hasHistory)
        {
            // Geçmiş iş ve fatura kayıtları korunur; müşteri yalnızca pasife alınır.
            customer.IsActive = false;
            await unitOfWork.SaveChangesAsync(ct);
            return new CustomerDeleteResult(false, true, "Müşterinin geçmiş kayıtları bulunduğu için silinmedi, pasife alındı.");
        }

        customers.Remove(customer);
        await unitOfWork.SaveChangesAsync(ct);
        return new CustomerDeleteResult(true, false, "Müşteri silindi.");
    }

    public async Task<CustomerSummaryDto> GetSummaryAsync(Guid id, CancellationToken ct)
    {
        var customer = await GetAsync(id, ct);
        var jobQuery = jobs.QueryAsNoTracking().Where(j => j.CustomerId == id);

        // Sayılar ve toplamlar tek sorguda veritabanında hesaplanır.
        var stats = await jobQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Completed = g.Count(j => j.Status == JobStatus.Completed),
                Amount = g.Where(j => j.Status == JobStatus.Completed).Sum(j => (decimal?)j.Price) ?? 0m,
            })
            .FirstOrDefaultAsync(ct);

        var invoiceTotal = await invoices.QueryAsNoTracking()
            .Where(i => i.CustomerId == id && i.Status == InvoiceStatus.Issued)
            .SumAsync(i => (decimal?)i.GrandTotal, ct) ?? 0m;

        var recentJobs = await jobQuery.OrderByDescending(j => j.JobDate).ThenByDescending(j => j.CreatedAt).Take(10)
            .Select(JobMappings.Projection).ToListAsync(ct);

        var invoiceHistory = await invoices.QueryAsNoTracking().Where(i => i.CustomerId == id)
            .OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.CreatedAt).Take(20)
            .Select(InvoiceMappings.ListProjection).ToListAsync(ct);

        return new CustomerSummaryDto(customer, stats?.Total ?? 0, stats?.Completed ?? 0, stats?.Amount ?? 0m, invoiceTotal, recentJobs, invoiceHistory);
    }

    public async Task EnsureExistsAsync(Guid id, CancellationToken ct)
    {
        if (!await customers.QueryAsNoTracking().AnyAsync(c => c.Id == id, ct)) throw NotFoundException.For("Müşteri");
    }
}
