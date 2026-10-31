using System.Linq.Expressions;
using AknaTransport.Business.Common;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using FluentValidation;

namespace AknaTransport.Business.Features.Invoices;

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
    DateTime CreatedAt);

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

public static class InvoiceMappings
{
    public static readonly Expression<Func<Invoice, InvoiceListItemDto>> ListProjection = i => new InvoiceListItemDto(
        i.Id, i.InvoiceNumber, i.CustomerId, i.Customer!.CompanyName, i.InvoiceDate, i.SubTotal, i.VatRate, i.VatAmount, i.GrandTotal,
        i.Currency, i.Status, i.Status == InvoiceStatus.Issued, i.EArchiveInvoiceNumber, i.Items.Count, i.IssuedAt, i.CreatedAt);
}

public sealed class CreateInvoiceRequestValidator : AbstractValidator<CreateInvoiceRequest>
{
    public CreateInvoiceRequestValidator()
    {
        RuleFor(x => x.JobIds).NotEmpty().WithMessage("En az bir iş seçilmelidir.")
            .Must(ids => ids.Count <= 500).WithMessage("Bir faturada en fazla 500 iş bulunabilir.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Aynı iş birden fazla kez seçilemez.");
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100).When(x => x.VatRate.HasValue).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class UpdateInvoiceRequestValidator : AbstractValidator<UpdateInvoiceRequest>
{
    public UpdateInvoiceRequestValidator()
    {
        RuleFor(x => x.JobIds).NotEmpty().WithMessage("En az bir iş seçilmelidir.")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Aynı iş birden fazla kez seçilemez.");
        RuleFor(x => x.InvoiceDate).NotEmpty().WithMessage("Fatura tarihi zorunludur.");
        RuleFor(x => x.VatRate).InclusiveBetween(0, 100).WithMessage("KDV oranı 0 ile 100 arasında olmalıdır.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
