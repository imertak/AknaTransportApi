namespace AknaTransport.Domain.Exceptions;

/// <summary>Domain kuralı ihlali. API katmanında 409 Conflict olarak döner.</summary>
public class DomainException(string message) : Exception(message);

public sealed class InvalidJobStatusTransitionException(Enums.JobStatus from, string action)
    : DomainException($"İş '{from}' durumundayken '{action}' işlemi yapılamaz.")
{
    public Enums.JobStatus From { get; } = from;
}

public sealed class InvalidInvoiceStateException(string message) : DomainException(message);
