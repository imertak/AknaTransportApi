using AknaTransportApi.Domain.Common;
using AknaTransportApi.Domain.Enums;

namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// İşin yükleme veya teslimat noktası. Sıra (Sequence) her tip için 1'den başlar;
/// iş açıklaması ve PDF'ler önce tüm yükleme, ardından tüm teslimat noktalarını bu sırayla gösterir.
/// </summary>
public class TransportJobStop : Entity
{
    public const int AddressMaxLength = 500;

    public Guid TransportJobId { get; set; }
    public TransportJob? TransportJob { get; set; }
    public StopType StopType { get; set; }
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Sequence { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Yeni durak bilgisi (iş oluşturma / güncelleme).</summary>
public sealed record JobStopInput(StopType StopType, string Address, double? Latitude, double? Longitude, string? Description);
