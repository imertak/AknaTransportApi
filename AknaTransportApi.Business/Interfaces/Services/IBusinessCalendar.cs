using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Interfaces.Services;

/// <summary>"Bugün" ve "bu ay" hesapları için işletmenin saat dilimi (varsayılan Europe/Istanbul).</summary>
public interface IBusinessCalendar
{
    DateOnly Today { get; }
    TimeZoneInfo TimeZone { get; }
    DateTime ToLocal(DateTime utc);
}
