namespace AknaTransportApi.Business.Dtos.Integrations;

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
    DateTime GeneratedAtUtc)
{
    /// <summary>true: e-Arşiv'de kesilmiş fatura (filigransız, GİB numarası ve ETTN ile).</summary>
    public bool Official { get; init; }
    public string? EArchiveInvoiceNumber { get; init; }
    public string? Ettn { get; init; }
    public string? WithholdingLabel { get; init; }
    public decimal WithholdingAmount { get; init; }
    public decimal? PayableAmount { get; init; }
    public string? AmountInWords { get; init; }
    public string? Iban { get; init; }
    public string? BankName { get; init; }
    public string? Notes { get; init; }
    public string CompanyName { get; init; } = "AKNA Transport";
}
