using FluentValidation;

namespace AknaTransportApi.Business.Validators.Locations;

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage("İş zorunludur.");
        RuleFor(x => x).Must(x => x.AllPoints().Count > 0).WithName("points").WithMessage("En az bir konum gönderilmelidir.");
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null)).WithName("latitude")
            .WithMessage("Enlem ve boylam birlikte gönderilmelidir.");
        RuleFor(x => x.Points!.Count).LessThanOrEqualTo(200).When(x => x.Points is not null).WithMessage("Tek istekte en fazla 200 konum gönderilebilir.");
        RuleForEach(x => x.AllPoints()).ChildRules(p =>
        {
            p.RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).WithMessage("Enlem -90 ile 90 arasında olmalıdır.");
            p.RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).WithMessage("Boylam -180 ile 180 arasında olmalıdır.");
            p.RuleFor(x => x.Accuracy).GreaterThanOrEqualTo(0);
            p.RuleFor(x => x.Speed).GreaterThanOrEqualTo(0);
            p.RuleFor(x => x.Heading).InclusiveBetween(0, 360);
        }).OverridePropertyName("points");
    }
}
