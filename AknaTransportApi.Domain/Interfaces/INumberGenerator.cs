namespace AknaTransportApi.Domain.Interfaces;

/// <summary>Benzersiz ve sıralı iş / fatura / teklif numarası üretir (ör. AKN-2026-000001).</summary>
public interface INumberGenerator
{
    Task<string> NextJobNumberAsync(DateOnly jobDate, CancellationToken cancellationToken = default);
    Task<string> NextInvoiceNumberAsync(DateOnly invoiceDate, CancellationToken cancellationToken = default);
    /// <summary>Nakliye teklifi numarası: AKT-2026-000001.</summary>
    Task<string> NextOfferNumberAsync(DateOnly offerDate, CancellationToken cancellationToken = default);
}
