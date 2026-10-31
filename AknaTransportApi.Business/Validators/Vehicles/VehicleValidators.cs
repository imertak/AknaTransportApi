using FluentValidation;

namespace AknaTransportApi.Business.Validators.Vehicles;

public sealed class VehicleRequestValidator : AbstractValidator<VehicleRequest>
{
    public VehicleRequestValidator()
    {
        RuleFor(x => x.PlateNumber).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Plaka zorunludur.")
            .MaximumLength(20)
            .Matches(@"^\s*\d{2}\s*[A-Za-zÇĞİÖŞÜçğıöşü]{1,3}\s*\d{2,5}\s*$").WithMessage("Geçerli bir plaka girin (ör. 34 ABC 123).");
        RuleFor(x => x.Brand).MaximumLength(100);
        RuleFor(x => x.Model).MaximumLength(100);
        RuleFor(x => x.VehicleType).IsInEnum().WithMessage("Geçerli bir araç tipi seçin.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public sealed class AssignVehicleRequestValidator : AbstractValidator<AssignVehicleRequest>
{
    public AssignVehicleRequestValidator() => RuleFor(x => x.DriverId).NotEmpty().WithMessage("Şoför seçin.");
}
