namespace AknaTransportApi.Business.Dtos.Vehicles;

/// <summary>Araç tipi. İş ve araç kayıtlarında <see cref="Code"/> saklanır, ekranda <see cref="Name"/> gösterilir.</summary>
public sealed record VehicleTypeDto(Guid Id, string Code, string Name, int SortOrder, bool IsActive);

public sealed class VehicleTypeRequest
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Listedeki sıra; boşsa yeni tip en sona eklenir, güncellemede değişmez.</summary>
    public int? SortOrder { get; set; }
    /// <summary>Güncellemede kullanılır; pasif tipler yeni iş ve araçlarda seçilemez.</summary>
    public bool IsActive { get; set; } = true;
}
