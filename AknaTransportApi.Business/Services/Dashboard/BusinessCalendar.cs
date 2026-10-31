using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Services.Dashboard;

public sealed class BusinessCalendar(TimeProvider clock, IOptions<AppOptions> options) : IBusinessCalendar
{
    private readonly Lazy<TimeZoneInfo> _zone = new(() => Resolve(options.Value.TimeZoneId));

    public TimeZoneInfo TimeZone => _zone.Value;

    public DateOnly Today => DateOnly.FromDateTime(ToLocal(clock.GetUtcNow().UtcDateTime));

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    private static TimeZoneInfo Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            // tzdata bulunmayan ortamlar için Türkiye'nin sabit UTC+3 ofseti.
            return TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
        }
    }
}
