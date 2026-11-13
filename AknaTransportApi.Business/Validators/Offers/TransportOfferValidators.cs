using FluentValidation;

namespace AknaTransportApi.Business.Validators.Offers;

public sealed class OfferCustomerRequestValidator : AbstractValidator<OfferCustomerRequest>
{
    public OfferCustomerRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().WithMessage("Müşteri firma adı zorunludur.").MaximumLength(200);
        RuleFor(x => x.ContactPerson).MaximumLength(150);
        RuleFor(x => x.PhoneNumber).MaximumLength(30).Matches(@"^\+?[0-9 ()\-]{7,20}$").When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber))
            .WithMessage("Geçerli bir telefon numarası girin.");
        RuleFor(x => x.Email).MaximumLength(200).Must(Text.IsValidEmail).When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Geçerli bir e-posta adresi girin.");
        RuleFor(x => x.Address).MaximumLength(TransportOffer.AddressMaxLength);
    }
}

public sealed class TransportOfferItemRequestValidator : AbstractValidator<TransportOfferItemRequest>
{
    public TransportOfferItemRequestValidator()
    {
        RuleFor(x => x.Description).NotEmpty().WithMessage("Kalem açıklaması girin.").MaximumLength(TransportOfferItem.DescriptionMaxLength);
        RuleFor(x => x.Unit).MaximumLength(TransportOfferItem.UnitMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Miktar sıfırdan büyük olmalıdır.")
            .LessThanOrEqualTo(100_000).WithMessage("Miktar çok yüksek.")
            .Must(q => decimal.Round(q, 3) == q).WithMessage("Miktar en fazla 3 ondalık basamak içerebilir.");
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("Birim fiyat negatif olamaz.")
            .LessThan(100_000_000).WithMessage("Birim fiyat çok yüksek.")
            .Must(p => decimal.Round(p, 2) == p).WithMessage("Birim fiyat en fazla 2 ondalık basamak içerebilir.");
    }
}

public sealed partial class TransportOfferRequestValidator : AbstractValidator<TransportOfferRequest>
{
    public static readonly IReadOnlyList<string> SupportedCurrencies = [Money.DefaultCurrency];

    public TransportOfferRequestValidator()
    {
        RuleFor(x => x.Customer).NotNull().SetValidator(new OfferCustomerRequestValidator());
        RuleFor(x => x.OfferDate).Must(d => d is null || d.Value.Year is >= 2000 and <= 2100).WithMessage("Geçerli bir teklif tarihi girin.");
        RuleFor(x => x.ValidUntil).Must((x, v) => v is null || x.OfferDate is null || v.Value >= x.OfferDate.Value)
            .WithMessage("Geçerlilik tarihi teklif tarihinden önce olamaz.");
        RuleFor(x => x.ValidUntil).Must((x, v) => v is null || x.OfferDate is null || v.Value <= x.OfferDate.Value.AddDays(365))
            .WithMessage("Geçerlilik süresi en fazla 1 yıl olabilir.");
        RuleFor(x => x.LoadingPoint).NotEmpty().WithMessage("Yükleme noktasını girin.").MaximumLength(TransportOffer.PointMaxLength);
        RuleFor(x => x.UnloadingPoint).NotEmpty().WithMessage("Boşaltma noktasını girin.").MaximumLength(TransportOffer.PointMaxLength);
        RuleFor(x => x.LoadingAddress).MaximumLength(TransportOffer.AddressMaxLength);
        RuleFor(x => x.UnloadingAddress).MaximumLength(TransportOffer.AddressMaxLength);
        RuleFor(x => x.TransportType).IsInEnum().WithMessage("Taşıma türünü seçin (Komple / Parsiyel).");
        RuleFor(x => x.VehicleType).NotEmpty().WithMessage("Araç tipini seçin.").MaximumLength(50);
        RuleFor(x => x.CargoDescription).MaximumLength(1000);
        RuleFor(x => x.DeliveryDate).Must((x, d) => d is null || x.LoadingDate is null || d.Value >= x.LoadingDate.Value)
            .WithMessage("Teslimat tarihi yükleme tarihinden önce olamaz.");
        RuleFor(x => x.DistanceKm).InclusiveBetween(0, 100_000).When(x => x.DistanceKm is not null).WithMessage("Mesafe 0 ile 100.000 km arasında olmalıdır.");

        RuleFor(x => x.Items).NotEmpty().WithMessage("En az bir fiyat kalemi ekleyin.")
            .Must(i => i.Count <= TransportOffer.MaxItems).WithMessage($"En fazla {TransportOffer.MaxItems} fiyat kalemi eklenebilir.");
        RuleForEach(x => x.Items).SetValidator(new TransportOfferItemRequestValidator());
        RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0).WithMessage("İndirim negatif olamaz.")
            .Must(p => decimal.Round(p, 2) == p).WithMessage("İndirim en fazla 2 ondalık basamak içerebilir.");
        // Toplamlar sunucuda hesaplanır; burada yalnızca anlaşılır hata mesajı için ön kontrol yapılır.
        RuleFor(x => x).Must(x => x.DiscountAmount < SubTotal(x)).When(x => x.Items.Count > 0 && x.Items.All(i => i.Quantity > 0 && i.UnitPrice >= 0))
            .WithName("discountAmount").WithMessage("İndirim, ara toplamdan küçük olmalıdır (teklif tutarı sıfırdan büyük olmalı).");
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100).When(x => x.VatRate is not null).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.");
        RuleFor(x => x.Currency).Must(c => c is null || SupportedCurrencies.Contains(c))
            .WithMessage("Şu an yalnızca TRY para birimi desteklenmektedir (nakliye işleri ve faturalar TRY ile tutulur).");

        RuleFor(x => x.PaymentTerms).MaximumLength(TransportOffer.TextMaxLength);
        RuleFor(x => x.DeliveryTerms).MaximumLength(TransportOffer.TextMaxLength);
        RuleFor(x => x.CustomerNotes).MaximumLength(TransportOffer.TextMaxLength);
        RuleFor(x => x.InternalNotes).MaximumLength(TransportOffer.TextMaxLength);
    }
}

public sealed partial class TransportOfferRequestValidator
{
    private static decimal SubTotal(TransportOfferRequest r) =>
        r.Items.Sum(i => Money.Round(i.Quantity * Money.Round(i.UnitPrice)));
}

public sealed class OfferStatusRequestValidator : AbstractValidator<OfferStatusRequest>
{
    public OfferStatusRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
