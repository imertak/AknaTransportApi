using System.Linq.Expressions;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Features.Jobs;
using AknaTransport.Domain.Entities;
using FluentValidation;

namespace AknaTransport.Business.Features.Customers;

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

public static class CustomerMappings
{
    public static readonly Expression<Func<Customer, CustomerDto>> Projection = c => new CustomerDto(
        c.Id, c.CompanyName, c.ContactPerson, c.PhoneNumber, c.Email, c.TaxOffice, c.TaxNumber, c.Address, c.Description,
        c.IsActive, c.CreatedAt, c.UpdatedAt);

    private static readonly Func<Customer, CustomerDto> Compiled = Projection.Compile();

    public static CustomerDto ToDto(this Customer customer) => Compiled(customer);

    public static void Apply(this Customer customer, CustomerRequest request)
    {
        customer.CompanyName = request.CompanyName.Trim();
        customer.ContactPerson = request.ContactPerson.TrimOrNull();
        customer.PhoneNumber = request.PhoneNumber.TrimOrNull();
        customer.Email = request.Email.TrimOrNull();
        customer.TaxOffice = request.TaxOffice.TrimOrNull();
        customer.TaxNumber = request.TaxNumber.TrimOrNull();
        customer.Address = request.Address.TrimOrNull();
        customer.Description = request.Description.TrimOrNull();
    }
}

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Firma adı zorunludur.").MaximumLength(200);
        RuleFor(x => x.ContactPerson).MaximumLength(150);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^\+?[0-9 ()\-]{7,20}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Geçerli bir telefon numarası girin.");
        RuleFor(x => x.Email).MaximumLength(200).Must(Text.IsValidEmail).When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Geçerli bir e-posta adresi girin.");
        RuleFor(x => x.TaxOffice).MaximumLength(100);
        // VKN 10, TCKN 11 hanelidir.
        RuleFor(x => x.TaxNumber).Matches(@"^\d{10,11}$").When(x => !string.IsNullOrWhiteSpace(x.TaxNumber))
            .WithMessage("Vergi numarası 10 (VKN) veya 11 (TCKN) haneli olmalıdır.");
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
