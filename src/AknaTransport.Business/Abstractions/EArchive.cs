namespace AknaTransport.Business.Abstractions;

/// <summary>
/// e-Arşiv entegratörü soyutlaması. Somut sağlayıcılar (Infrastructure) bu arayüzü
/// adapter olarak uygular. Sağlayıcı yapılandırılmamışsa <see cref="IsConfigured"/> false döner
/// ve hiçbir fatura Issued durumuna geçirilmez.
/// </summary>
public interface IEArchiveInvoiceService
{
    bool IsConfigured { get; }

    /// <summary>Yapılandırılmış sağlayıcının adı (ör. "Uyumsoft"). Yapılandırılmamışsa null.</summary>
    string? ProviderName { get; }

    Task<EArchiveSendResult> SendInvoiceAsync(EArchiveInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<EArchiveStatusResult> GetStatusAsync(string reference, CancellationToken cancellationToken = default);
    Task<byte[]> GetPdfAsync(string reference, CancellationToken cancellationToken = default);
    Task<EArchiveCancelResult> CancelAsync(string reference, string reason, CancellationToken cancellationToken = default);
}

public enum EArchiveDocumentStatus
{
    /// <summary>Sağlayıcı belgeyi aldı, işleniyor.</summary>
    Processing = 0,
    /// <summary>Fatura resmi olarak kesildi.</summary>
    Issued = 1,
    /// <summary>Sağlayıcı reddetti.</summary>
    Rejected = 2,
    Cancelled = 3,
}

public sealed record EArchiveParty(string Name, string? TaxNumber, string? TaxOffice, string? Address, string? Email, string? PhoneNumber);

public sealed record EArchiveLine(string Description, decimal Quantity, decimal UnitPrice, decimal VatRate, decimal LineTotal, decimal VatAmount);

public sealed record EArchiveInvoiceRequest(
    Guid InvoiceId,
    string LocalInvoiceNumber,
    DateOnly InvoiceDate,
    string Currency,
    EArchiveParty Customer,
    IReadOnlyList<EArchiveLine> Lines,
    decimal SubTotal,
    decimal VatAmount,
    decimal GrandTotal,
    string? Notes);

public sealed record EArchiveSendResult(
    bool Accepted,
    EArchiveDocumentStatus Status,
    string? Reference,
    string? InvoiceNumber,
    string? InvoiceUrl,
    string? ErrorMessage);

public sealed record EArchiveStatusResult(EArchiveDocumentStatus Status, string? InvoiceNumber, string? InvoiceUrl, string? ErrorMessage);

public sealed record EArchiveCancelResult(bool Success, string? ErrorMessage);
