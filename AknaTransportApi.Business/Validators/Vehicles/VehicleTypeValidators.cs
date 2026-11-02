using FluentValidation;

namespace AknaTransportApi.Business.Validators.Vehicles;

public sealed class VehicleTypeRequestValidator : AbstractValidator<VehicleTypeRequest>
{
    public VehicleTypeRequestValidator()
    {
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Araç tipi adını girin.").MaximumLength(100);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000).When(x => x.SortOrder.HasValue);
    }
}
