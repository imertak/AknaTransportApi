namespace AknaTransportApi.Domain.Interfaces;

/// <summary>Benzersiz ve sıralı iş / fatura numarası üretir (ör. AKN-2026-000001).</summary>
public interface INumberGenerator
{
    Task<string> NextJobNumberAsync(DateOnly jobDate, CancellationToken cancellationToken = default);
    Task<string> NextInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken cancellationToken = default);
}
