namespace AknaTransportApi.Business.Dtos.Vehicles;

/// <summary>Araç ve (varsa) şu anki aktif şoförü.</summary>
public sealed record VehicleDto(
    Guid Id,
    string PlateNumber,
    string? Brand,
    string? Model,
    VehicleType VehicleType,
    bool IsActive,
    string? Description,
    Guid? DriverId,
    string? DriverName,
    DateTime? AssignedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed class VehicleRequest
{
    public string PlateNumber { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public VehicleType VehicleType { get; set; }
    public string? Description { get; set; }
    /// <summary>Güncellemede kullanılır; yeni araçlar aktif oluşturulur.</summary>
    public bool IsActive { get; set; } = true;
}

public sealed class AssignVehicleRequest
{
    public Guid DriverId { get; set; }
}

public sealed class VehicleQuery : PagedQuery
{
    /// <summary>Yalnızca bu şoföre aktif olarak atanmış araçlar.</summary>
    public Guid? DriverId { get; set; }
    public bool? IsActive { get; set; }
    /// <summary>Plaka, marka veya modelde arama.</summary>
    public string? Search { get; set; }
}
