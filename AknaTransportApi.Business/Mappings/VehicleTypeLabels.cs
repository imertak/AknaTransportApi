using System.Linq.Expressions;

namespace AknaTransportApi.Business.Mappings;

/// <summary>Araç tiplerinin Türkçe karşılıkları (PDF ve e-posta için).</summary>
public static class VehicleTypeLabels
{
    public static string ToTurkish(this VehicleType type) => type switch
    {
        VehicleType.Panelvan => "Panelvan",
        VehicleType.SmallPanelvan => "Küçük Panelvan",
        VehicleType.Pickup => "Kamyonet",
        VehicleType.Truck => "Kamyon",
        _ => "Diğer",
    };
}
