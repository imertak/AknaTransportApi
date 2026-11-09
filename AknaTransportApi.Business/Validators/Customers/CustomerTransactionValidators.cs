using FluentValidation;

namespace AknaTransportApi.Business.Validators.Customers;

public sealed class CustomerTransactionRequestValidator : AbstractValidator<CustomerTransactionRequest>
{
    public CustomerTransactionRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Geçersiz hareket türü.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Tutar sıfırdan büyük olmalıdır.")
            .LessThan(1_000_000_000_000m).WithMessage("Tutar çok büyük.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Tutar en fazla 2 ondalık basamak içerebilir.");
        RuleFor(x => x.Date).NotEqual(default(DateOnly)).WithMessage("Tarih zorunludur.");
        RuleFor(x => x.Method).MaximumLength(CustomerTransaction.MethodMaxLength);
        RuleFor(x => x.Description).MaximumLength(CustomerTransaction.DescriptionMaxLength);
    }
}
