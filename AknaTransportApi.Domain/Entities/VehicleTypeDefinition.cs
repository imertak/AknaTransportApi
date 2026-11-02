namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Yöneticinin tanımladığı araç tipi (ör. Panelvan, Kamyon, Tır). İş ve araç kayıtları <see cref="Code"/> değerini saklar.
/// Kullanımdaki tipler silinmez; pasife alınır ve yeni kayıtlarda seçilemez, eski kayıtlarda adıyla görünmeye devam eder.
/// </summary>
public class VehicleTypeDefinition : Entity, IHasTimestamps
{
    /// <summary>Değişmeyen kod (ör. "Panelvan"). Ad değişse de kayıtlar bu koda bağlı kalır.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
