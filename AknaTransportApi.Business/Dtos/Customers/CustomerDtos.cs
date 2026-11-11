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
    DateTime? UpdatedAt,
    [property: System.Text.Json.Serialization.JsonIgnore] string? AdditionalEmails = null)
{
    private readonly IReadOnlyList<string>? _emails;

    /// <summary>Tüm e-posta adresleri; ilki ana adrestir (<see cref="Email"/>).</summary>
    public IReadOnlyList<string> Emails
    {
        get => _emails ?? Customer.MergeEmails(Email, AdditionalEmails);
        init => _emails = value;
    }
}

/// <summary>Müşteri oluşturma / güncelleme isteği.</summary>
public sealed class CustomerRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    /// <summary>Eski istemciler için tek adres. <see cref="Emails"/> gönderilirse yok sayılır.</summary>
    public string? Email { get; set; }
    /// <summary>Tüm e-posta adresleri; ilki ana adres olur. Gönderilmezse (null) Email kullanılır ve ek adresler korunur.</summary>
    public List<string>? Emails { get; set; }
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

/// <summary>Cari hesaba elle girilen hareket: tahsilat (gelen), iade (giden) veya faturasız borç kaydı.</summary>
public sealed record CustomerTransactionRequest(CustomerTransactionType Type, decimal Amount, DateOnly Date, string? Method, string? Description);

public sealed record CustomerTransactionDto(
    Guid Id,
    Guid CustomerId,
    CustomerTransactionType Type,
    decimal Amount,
    DateOnly Date,
    string? Method,
    string? Description,
    string? CreatedByName,
    DateTime CreatedAt);

/// <summary>
/// Müşterinin cari durumu. Balance = kesilen faturalar (ödenecek tutar) + borç kayıtları + iadeler − tahsilatlar.
/// Pozitif bakiye müşterinin borcu, negatif bakiye müşterinin alacağıdır (fazla ödeme).
/// </summary>
public sealed record CustomerBalanceDto(
    Guid CustomerId,
    string CompanyName,
    bool IsActive,
    decimal Invoiced,
    int InvoiceCount,
    decimal Charged,
    decimal Collected,
    decimal Refunded,
    decimal Balance,
    DateOnly? LastMovementDate);

public sealed record CustomerBalanceTotalsDto(decimal Invoiced, decimal Charged, decimal Collected, decimal Refunded, decimal Balance, int CustomersWithBalance);

/// <summary>Bir aydaki hareketler (fatura tarihi / hareket tarihine göre). Year/Month takvim ayıdır.</summary>
public sealed record CustomerBalanceMonthDto(int Year, int Month, decimal Invoiced, int InvoiceCount, decimal Charged, decimal Collected, decimal Refunded);

/// <summary>
/// Totals tüm dönemlerin toplamıdır (bakiye dahil). Months aynı müşterilerin hareketlerini ay ay verir (yeniden eskiye);
/// hareket olmayan aylar yer almaz.
/// </summary>
public sealed record CustomerBalanceOverviewDto(CustomerBalanceTotalsDto Totals, IReadOnlyList<CustomerBalanceDto> Items, IReadOnlyList<CustomerBalanceMonthDto>? Months = null);

public sealed class CustomerBalanceQuery
{
    public string? Search { get; set; }
    /// <summary>true ise yalnızca bakiyesi sıfır olmayan müşteriler.</summary>
    public bool OpenOnly { get; set; }
}

/// <summary>Cari hesap ekstresindeki satır türü.</summary>
public enum LedgerEntryKind
{
    Invoice = 0,
    Collection = 1,
    Refund = 2,
    Charge = 3,
}

/// <summary>Ekstre satırı. Debit bakiyeyi artırır (borç), Credit düşürür (alacak). Balance, satırdan sonraki bakiyedir.</summary>
public sealed record LedgerEntryDto(
    LedgerEntryKind Kind,
    Guid Id,
    DateOnly Date,
    string Title,
    string? Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    Guid? InvoiceId,
    Guid? TransactionId,
    string? Method,
    DateTime CreatedAt);

public sealed record CustomerLedgerDto(CustomerBalanceDto Balance, IReadOnlyList<LedgerEntryDto> Entries);
