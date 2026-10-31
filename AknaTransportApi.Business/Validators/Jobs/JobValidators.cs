using FluentValidation;

namespace AknaTransportApi.Business.Validators.Jobs;

public abstract class JobRequestBaseValidator<T> : AbstractValidator<T> where T : JobRequestBase
{
    protected JobRequestBaseValidator()
    {
        RuleFor(x => x.JobDate).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("İş tarihi zorunludur.")
            .Must(d => d.Year is >= 2000 and <= 2100).WithMessage("Geçerli bir iş tarihi girin.");
        RuleFor(x => x.StoreName).NotEmpty().WithMessage("Mağaza / firma adı zorunludur.").MaximumLength(200);
        RuleFor(x => x.VehicleType).IsInEnum().WithMessage("Geçerli bir araç tipi seçin.");
        RuleFor(x => x).Must(x => x.ResolveStops().Any(s => s.StopType == StopType.Pickup))
            .WithName("stops").WithMessage("En az bir yükleme adresi girin.");
        RuleFor(x => x).Must(x => x.ResolveStops().Any(s => s.StopType == StopType.Delivery))
            .WithName("stops").WithMessage("En az bir teslimat adresi girin.");
        RuleFor(x => x.Stops!.Count).LessThanOrEqualTo(30).When(x => x.Stops is not null)
            .OverridePropertyName("stops").WithMessage("Bir işte en fazla 30 durak olabilir.");
        RuleForEach(x => x.Stops).SetValidator(new JobStopRequestValidator()).When(x => x.Stops is not null);
        RuleFor(x => x.PickupAddress).MaximumLength(TransportJobStop.AddressMaxLength);
        RuleFor(x => x.DeliveryAddress).MaximumLength(TransportJobStop.AddressMaxLength);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Fiyat sıfırdan büyük olmalıdır.")
            .LessThan(100_000_000).WithMessage("Fiyat çok yüksek.")
            .Must(p => decimal.Round(p, 2) == p).WithMessage("Fiyat en fazla 2 ondalık basamak içerebilir.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.PickupLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.DeliveryLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.PickupLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.DeliveryLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x).Must(x => (x.PickupLatitude is null) == (x.PickupLongitude is null))
            .WithMessage("Yükleme konumu için enlem ve boylam birlikte gönderilmelidir.");
        RuleFor(x => x).Must(x => (x.DeliveryLatitude is null) == (x.DeliveryLongitude is null))
            .WithMessage("Teslimat konumu için enlem ve boylam birlikte gönderilmelidir.");
    }
}

public sealed class JobStopRequestValidator : AbstractValidator<JobStopRequest>
{
    public JobStopRequestValidator()
    {
        RuleFor(x => x.StopType).IsInEnum().WithMessage("Geçerli bir durak tipi seçin (Pickup / Delivery).");
        RuleFor(x => x.Address).NotEmpty().WithMessage("Durak adresi boş olamaz.").MaximumLength(TransportJobStop.AddressMaxLength);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).WithMessage("Enlem -90 ile 90 arasında olmalıdır.");
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).WithMessage("Boylam -180 ile 180 arasında olmalıdır.");
        RuleFor(x => x).Must(x => (x.Latitude is null) == (x.Longitude is null)).WithName("latitude")
            .WithMessage("Durak konumu için enlem ve boylam birlikte gönderilmelidir.");
    }
}

public sealed class CreateJobRequestValidator : JobRequestBaseValidator<CreateJobRequest>
{
    public CreateJobRequestValidator()
    {
        RuleFor(x => x).Must(x => x.CustomerId.HasValue ^ (x.NewCustomer is not null))
            .WithName(nameof(CreateJobRequest.CustomerId))
            .WithMessage("Mevcut bir müşteri seçin veya yeni müşteri bilgisi girin (ikisi birden olamaz).");
        RuleFor(x => x.NewCustomer!).SetValidator(new CustomerRequestValidator()).When(x => x.NewCustomer is not null);
        RuleFor(x => x).Must(x => !(x.AssignToMe && x.AssignedDriverId.HasValue))
            .WithName(nameof(CreateJobRequest.AssignedDriverId))
            .WithMessage("İşi kendinize atarken ayrıca şoför seçemezsiniz.");
        RuleFor(x => x.AssignedVehicleId).Must((x, v) => v is null || x.AssignToMe || x.AssignedDriverId.HasValue)
            .WithMessage("Araç seçmek için önce şoför seçin.");
        RuleFor(x => x.Currency).Must(c => c is null || c == "TRY").WithMessage("Şu an yalnızca TRY para birimi desteklenmektedir.");
    }
}

public sealed class UpdateJobRequestValidator : JobRequestBaseValidator<UpdateJobRequest>
{
    public UpdateJobRequestValidator() => RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Müşteri zorunludur.");
}

public sealed class CancelJobRequestValidator : AbstractValidator<CancelJobRequest>
{
    public CancelJobRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}

public sealed class JobActionRequestValidator : AbstractValidator<JobActionRequest>
{
    public JobActionRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public sealed class AssignJobRequestValidator : AbstractValidator<AssignJobRequest>
{
    public AssignJobRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.VehicleId).Null().When(x => x.DriverId is null).WithMessage("Araç seçmek için önce şoför seçin.");
    }
}

public sealed class ClaimJobRequestValidator : AbstractValidator<ClaimJobRequest>
{
    public ClaimJobRequestValidator() => RuleFor(x => x.Note).MaximumLength(500);
}
