using FluentValidation;
using System.Linq.Expressions;

namespace AknaTransportApi.Business.Dtos.Invoices;

public sealed record InvoiceListItemDto(
    Guid Id,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    DateOnly InvoiceDate,
    decimal SubTotal,
    decimal VatRate,
    decimal VatAmount,
    decimal GrandTotal,
    string Currency,
    InvoiceStatus Status,
    // true yalnızca gerçek e-Arşiv sağlayıcısından kesilmiş faturalar için.
    bool IsOfficial,
    string? EArchiveInvoiceNumber,
    int JobCount,
    DateTime? IssuedAt,
    DateTime CreatedAt,
    // Liste ekranlarının detay çağrısı yapmadan iş ve açıklama gösterebilmesi için.
    Guid? FirstJobId,
    string? FirstJobNumber,
    string? Description,
    string? CustomerEmail);

public sealed record InvoiceItemDto(
    Guid Id,
    Guid JobId,
    string JobNumber,
    DateOnly JobDate,
    string StoreName,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal LineTotal);

public sealed record InvoiceEmailDto(Guid Id, string RecipientEmail, string Subject, EmailStatus Status, int AttemptCount, string? LastError, DateTime? SentAt, DateTime CreatedAt);

public sealed record InvoiceDetailDto(
    Guid Id,
    string InvoiceNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    DateOnly InvoiceDate,
    decimal SubTotal,
    decimal VatRate,
    decimal VatAmount,
    decimal GrandTotal,
    string Currency,
    InvoiceStatus Status,
    bool IsOfficial,
    string? Notes,
    string? EArchiveProvider,
    string? EArchiveReference,
    string? EArchiveInvoiceNumber,
    string? EArchiveInvoiceUrl,
    string? LastError,
    Guid CreatedByUserId,
    DateTime? IssuedAt,
    DateTime? CancelledAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<InvoiceItemDto> Items,
    InvoiceEmailDto? Email);

public sealed class CreateInvoiceRequest
{
    /// <summary>Faturalanacak tamamlanmış işler. Hepsi aynı müşteriye ait olmalıdır.</summary>
    public List<Guid> JobIds { get; set; } = [];
    /// <summary>Boş bırakılırsa bugünün tarihi kullanılır.</summary>
    public DateOnly? InvoiceDate { get; set; }
    /// <summary>Boş bırakılırsa varsayılan KDV oranı (%20) kullanılır.</summary>
    public decimal? VatRate { get; set; }
    public string? Notes { get; set; }
}

public sealed class UpdateInvoiceRequest
{
    public List<Guid> JobIds { get; set; } = [];
    public DateOnly InvoiceDate { get; set; }
    public decimal VatRate { get; set; }
    public string? Notes { get; set; }
}

public sealed class CancelInvoiceRequest
{
    public string? Reason { get; set; }
}

public sealed class InvoiceQuery : PagedQuery
{
    public Guid? CustomerId { get; set; }
    public InvoiceStatus? Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Search { get; set; }
}

public sealed record FileDownload(byte[] Content, string FileName, string ContentType = "application/pdf");
