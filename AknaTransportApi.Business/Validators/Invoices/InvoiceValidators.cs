using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Validators.Invoices;

public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.JobIds).NotEmpty().WithMessage("En az bir iş seçilmelidir.")
            .Must(ids => ids.Count <= 500).WithMessage("Bir faturada en fazla 500 iş bulunabilir.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Aynı iş birden fazla kez seçilemez.");
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100).When(x => x.VatRate.HasValue).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.JobIds).NotEmpty().WithMessage("En az bir iş seçilmelidir.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Aynı iş birden fazla kez seçilemez.");
        RuleFor(x => x.InvoiceDate).NotEmpty().WithMessage("Fatura tarihi zorunludur.");
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
