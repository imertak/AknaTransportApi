using AknaTransport.Business.Common;
using AknaTransport.Domain.Enums;
using FluentValidation;

namespace AknaTransport.Business.Features.Locations;

public sealed record LocationDto(
    Guid JobId,
    Guid DriverId,
    double Latitude,
    double Longitude,
    double? Accuracy,
    double? Speed,
    double? Heading,
    DateTime RecordedAt);

public sealed class LocationPointRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Metre.</summary>
    public double? Accuracy { get; set; }
    /// <summary>m/s.</summary>
    public double? Speed { get; set; }
    /// <summary>Derece (0-360).</summary>
    public double? Heading { get; set; }
    /// <summary>Cihazda konumun alındığı an (UTC). Boşsa sunucu zamanı kullanılır.</summary>
    public DateTime? RecordedAt { get; set; }
}

/// <summary>
/// Konum güncellemesi. Tek nokta için üst seviye alanlar, çevrimdışı biriken
/// konumlar için <see cref="Points"/> kullanılır.
/// </summary>
public sealed class UpdateLocationRequest
{
    public Guid JobId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public DateTime? RecordedAt { get; set; }
    public List<LocationPointRequest>? Points { get; set; }

    public IReadOnlyList<LocationPointRequest> AllPoints()
    {
        var list = new List<LocationPointRequest>();
        if (Latitude is { } lat && Longitude is { } lng)
            list.Add(new LocationPointRequest { Latitude = lat, Longitude = lng, Accuracy = Accuracy, Speed = Speed, Heading = Heading, RecordedAt = RecordedAt });
        if (Points is not null) list.AddRange(Points);
        return list;
    }
}

public sealed record LocationUpdateResult(int Received, int Stored, int Ignored, LocationDto? Latest);

public sealed class LocationHistoryQuery : PagedQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public LocationHistoryQuery() => PageSize = 100;
}

public sealed record ActiveJobLocationDto(
    Guid JobId,
    string JobNumber,
    JobStatus Status,
    string CustomerName,
    string StoreName,
    VehicleType VehicleType,
    string PickupAddress,
    string DeliveryAddress,
    Guid? DriverId,
    string? DriverName,
    string? DriverPhoneNumber,
    LocationDto? Location);

public sealed record TrackingTokenDto(string Token, string? Url, DateTime ExpiresAt);

/// <summary>Müşteri takip bağlantısı yanıtı: fiyat ve iç bilgiler içermez.</summary>
public sealed record PublicTrackingDto(string JobNumber, JobStatus Status, string StoreName, DateOnly JobDate, string DeliveryAddress, LocationDto? Location);

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

public static class Geo
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
