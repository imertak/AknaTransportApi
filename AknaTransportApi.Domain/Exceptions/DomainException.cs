namespace AknaTransportApi.Domain.Exceptions;

/// <summary>Domain kuralı ihlali. API katmanında 409 Conflict olarak döner.</summary>
public class DomainException(string message) : Exception(message);

public sealed class InvalidJobStatusTransitionException(Enums.JobStatus from, string action)
    : DomainException($"İş '{from}' durumundayken '{action}' işlemi yapılamaz.")
{
    public Enums.JobStatus From { get; } = from;
}

public sealed class InvalidInvoiceStateException(string message) : DomainException(message);

public sealed class InvalidOfferStatusTransitionException(Enums.TransportOfferStatus from, string action, string? message = null)
    : DomainException(message ?? $"Teklif '{Label(from)}' durumundayken {action} işlemi yapılamaz.")
{
    public Enums.TransportOfferStatus From { get; } = from;

    public static string Label(Enums.TransportOfferStatus status) => status switch
    {
        Enums.TransportOfferStatus.Draft => "Taslak",
        Enums.TransportOfferStatus.Pending => "Yanıt bekleniyor",
        Enums.TransportOfferStatus.Accepted => "Kabul edildi",
        Enums.TransportOfferStatus.Rejected => "Reddedildi",
        Enums.TransportOfferStatus.Expired => "Süresi doldu",
        Enums.TransportOfferStatus.Cancelled => "İptal edildi",
        _ => status.ToString(),
    };
}
