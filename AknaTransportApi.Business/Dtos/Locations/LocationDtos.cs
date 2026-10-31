using FluentValidation;

namespace AknaTransportApi.Business.Dtos.Locations;

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
