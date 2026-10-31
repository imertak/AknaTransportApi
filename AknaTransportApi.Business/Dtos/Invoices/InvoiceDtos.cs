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
    string? CustomerEmail,
    string? WithholdingCode,
    decimal WithholdingAmount,
    decimal PayableAmount);

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
    InvoiceEmailDto? Email,
    // Tevkifat (kod boşsa yok). Oran KDV'nin yüzdesidir: 2/10 → 20.
    string? WithholdingCode,
    string? WithholdingName,
    decimal WithholdingRate,
    decimal WithholdingAmount,
    decimal PayableAmount,
    /// "YALNIZ: ... TÜRK LİRASI ..." (ödenecek tutarın yazıyla gösterimi).
    string AmountInWords,
    string? Iban,
    string? BankName,
    /// Faturada/e-Arşiv notunda yer alan tam açıklama (yazıyla tutar + IBAN + not).
    string DocumentNote,
    /// true: fatura sağlayıcıda taslak olarak bekliyor, SMS koduyla imzalanması gerekiyor (GİB portalı).
    bool RequiresSmsSignature);

public sealed record WithholdingTypeDto(string Code, int Rate, string Fraction, string Name);

public sealed record EArchiveSmsChallengeDto(string OperationId, string? MaskedPhone);

public sealed class SignInvoiceRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public sealed class CreateInvoiceRequest
{
    /// <summary>Faturalanacak tamamlanmış işler. Hepsi aynı müşteriye ait olmalıdır.</summary>
    public List<Guid> JobIds { get; set; } = [];
    /// <summary>Boş bırakılırsa bugünün tarihi kullanılır.</summary>
    public DateOnly? InvoiceDate { get; set; }
    /// <summary>Boş bırakılırsa varsayılan KDV oranı (%20) kullanılır.</summary>
    public decimal? VatRate { get; set; }
    public string? Notes { get; set; }
    /// <summary>KDV tevkifat kodu (ör. "624"). Boş: tevkifat yok. Gönderilmezse App:DefaultWithholdingCode kullanılır.</summary>
    public string? WithholdingCode { get; set; }
    /// <summary>true ise tevkifat uygulanmaz (varsayılan kod tanımlı olsa bile).</summary>
    public bool NoWithholding { get; set; }
    /// <summary>Faturada yazılacak IBAN. Boşsa App:CompanyIban kullanılır.</summary>
    public string? Iban { get; set; }
}

public sealed class UpdateInvoiceRequest
{
    public List<Guid> JobIds { get; set; } = [];
    public DateOnly InvoiceDate { get; set; }
    public decimal VatRate { get; set; }
    public string? Notes { get; set; }
    /// <summary>KDV tevkifat kodu. Boş: tevkifat yok.</summary>
    public string? WithholdingCode { get; set; }
    /// <summary>Faturada yazılacak IBAN. Boşsa App:CompanyIban kullanılır.</summary>
    public string? Iban { get; set; }
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
