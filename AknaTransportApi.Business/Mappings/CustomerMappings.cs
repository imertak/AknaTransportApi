using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

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
