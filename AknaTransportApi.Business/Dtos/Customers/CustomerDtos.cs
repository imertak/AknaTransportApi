using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Dtos.Customers;

public sealed record CustomerDto(
    Guid Id,
    string CompanyName,
    string? ContactPerson,
    string? PhoneNumber,
    string? Email,
    string? TaxOffice,
    string? TaxNumber,
    string? Address,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>Müşteri oluşturma / güncelleme isteği.</summary>
public sealed class CustomerRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Address { get; set; }
    public string? Description { get; set; }
    /// <summary>Yalnızca güncellemede dikkate alınır.</summary>
    public bool IsActive { get; set; } = true;
}

public sealed class CustomerQuery : PagedQuery
{
    public string? Search { get; set; }
    /// <summary>Boş bırakılırsa yalnızca aktif müşteriler döner.</summary>
    public bool? IsActive { get; set; } = true;
}

public sealed record CustomerSummaryDto(
    CustomerDto Customer,
    int TotalJobs,
    int CompletedJobs,
    decimal TotalTransportAmount,
    decimal TotalInvoiceAmount,
    IReadOnlyList<JobDto> RecentJobs,
    IReadOnlyList<InvoiceListItemDto> Invoices);

public sealed record CustomerDeleteResult(bool Deleted, bool Deactivated, string Message);

/// <summary>Müşterinin geçmiş işlerinde kullanılmış adres (yeni iş formunda öneri olarak).</summary>
public sealed record CustomerAddressDto(StopType StopType, string Address, double? Latitude, double? Longitude, int UseCount, DateTime LastUsedAt);

public sealed class CustomerAddressQuery
{
    /// <summary>Boşsa hem yükleme hem teslimat adresleri döner.</summary>
    public StopType? StopType { get; set; }
    public string? Search { get; set; }
    public int Limit { get; set; } = 30;
}
