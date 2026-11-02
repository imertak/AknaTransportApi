using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Validators.Customers;

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public const int MaxEmails = 10;

    public CustomerRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Firma adı zorunludur.").MaximumLength(200);
        RuleFor(x => x.ContactPerson).MaximumLength(150);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^\+?[0-9 ()\-]{7,20}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Geçerli bir telefon numarası girin.");
        RuleFor(x => x.Email).MaximumLength(200).Must(Text.IsValidEmail).When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Geçerli bir e-posta adresi girin.");
        RuleFor(x => x.Emails!).Must(e => e.Count(s => !string.IsNullOrWhiteSpace(s)) <= MaxEmails).When(x => x.Emails is not null)
            .WithMessage($"En fazla {MaxEmails} e-posta adresi girilebilir.");
        RuleForEach(x => x.Emails).Must(e => string.IsNullOrWhiteSpace(e) || (e.Trim().Length <= 200 && Text.IsValidEmail(e.Trim()) && !e.Contains(Customer.EmailSeparator)))
            .WithMessage("Geçerli bir e-posta adresi girin: {PropertyValue}");
        RuleFor(x => x.TaxOffice).MaximumLength(100);
        // VKN 10, TCKN 11 hanelidir.
        RuleFor(x => x.TaxNumber).Matches(@"^\d{10,11}$").When(x => !string.IsNullOrWhiteSpace(x.TaxNumber))
            .WithMessage("Vergi numarası 10 (VKN) veya 11 (TCKN) haneli olmalıdır.");
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
