using System.Text.RegularExpressions;
using AknaTransportApi.Domain.Common;
using AknaTransportApi.Domain.Enums;

namespace AknaTransportApi.Domain.Entities;

/// <summary>Filo aracı. Pasif araçlar yeni işlere atanamaz; geçmiş işlerdeki kayıtları korunur.</summary>
public partial class Vehicle : Entity, IHasTimestamps
{
    public string PlateNumber { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public VehicleType VehicleType { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<DriverVehicle> DriverAssignments { get; set; } = new List<DriverVehicle>();

    /// <summary>Plakayı karşılaştırılabilir biçime getirir: büyük harf, tek boşluk (ör. "34 ABC 123").</summary>
    public static string NormalizePlate(string plate) => Spaces().Replace(plate.Trim().ToUpperInvariant(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>
/// Şoför-araç ataması. Bir şoförün birden fazla aktif aracı olabilir; bir araç aynı anda yalnızca
/// bir şoföre aktif olarak atanabilir (VehicleId üzerinde IsActive filtreli benzersiz indeks).
/// Atama kaldırıldığında kayıt silinmez, pasife alınır.
/// </summary>
public class DriverVehicle : Entity
{
    public Guid DriverId { get; set; }
    public User? Driver { get; set; }
    public Guid VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAt { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTime? UnassignedAt { get; set; }
}
