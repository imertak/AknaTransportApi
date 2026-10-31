using AknaTransport.Domain.Enums;

namespace AknaTransport.Business.Abstractions;

public sealed record JobReportRow(DateOnly Date, string StoreName, VehicleType VehicleType, string Description, decimal Price, string? CustomerName);

public sealed record JobReportDocument(
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    IReadOnlyList<JobReportRow> Rows,
    decimal TotalAmount,
    string Currency,
    DateTime GeneratedAtUtc,
    string? CustomerName = null,
    string? Subtitle = null);

/// <summary>Nakliye işleri dökümü PDF üretimi (Infrastructure / QuestPDF).</summary>
public interface IJobReportPdfGenerator
{
    byte[] Generate(JobReportDocument document);
}

public sealed record DraftInvoiceLine(string Description, decimal Quantity, decimal UnitPrice, decimal LineTotal);

public sealed record DraftInvoiceDocument(
    string InvoiceNumber,
    DateOnly InvoiceDate,
    InvoiceStatus Status,
    string CustomerName,
    string? CustomerTaxOffice,
    string? CustomerTaxNumber,
    string? CustomerAddress,
    IReadOnlyList<DraftInvoiceLine> Lines,
    decimal SubTotal,
    decimal VatRate,
    decimal VatAmount,
    decimal GrandTotal,
    string Currency,
    DateTime GeneratedAtUtc);

/// <summary>Taslak fatura önizleme PDF'i. Üzerinde "resmi fatura değildir" ibaresi bulunur.</summary>
public interface IDraftInvoicePdfGenerator
{
    byte[] Generate(DraftInvoiceDocument document);
}
