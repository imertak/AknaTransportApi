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
    decimal PayableAmount,
    // true: fatura başka yerde kesildi, PDF'i elle yüklendi.
    bool IsManual = false);

/// <summary>Fatura kalemi. İş alanları (JobId, JobNumber, JobDate, StoreName) işe bağlı olmayan serbest kalemde boştur.</summary>
public sealed record InvoiceItemDto(
    Guid Id,
    Guid? JobId,
    string? JobNumber,
    DateOnly? JobDate,
    string? StoreName,
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
    bool RequiresSmsSignature,
    /// Elle gönderilen son e-posta ("E-posta gönder"; taslak faturalar dahil).
    InvoiceEmailDto? ManualEmail = null,
    /// true: faturaya işler tek tek değil, tek kalem "Nakliye Bedeli" olarak yazılır (iş dökümü değişmez).
    bool SingleLine = false,
    /// true: fatura başka yerde kesildi, PDF'i elle yüklendi (e-Arşiv referansı yoktur).
    bool IsManual = false,
    /// true: fatura işlerden değil, elle girilen kalemlerden oluşur (işe bağlı olmayan serbest fatura).
    bool IsFreeForm = false);

public sealed record WithholdingTypeDto(string Code, int Rate, string Fraction, string Name);

public sealed record EArchiveSmsChallengeDto(string OperationId, string? MaskedPhone);

public sealed class SignInvoiceRequest
{
    public string OperationId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

/// <summary>İşe bağlı olmayan (serbest) fatura kalemi.</summary>
public sealed class InvoiceLineRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    /// <summary>KDV hariç birim fiyat.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Fatura oluşturma isteği. İki türü vardır:
/// <list type="bullet">
/// <item>İşlerden fatura: <see cref="JobIds"/> dolu; müşteri işlerden gelir, kalemler işlerden oluşur.</item>
/// <item>Serbest fatura: <see cref="JobIds"/> boş, <see cref="Lines"/> dolu; müşteri <see cref="CustomerId"/> (kayıtlı)
/// veya <see cref="NewCustomer"/> (fatura ile birlikte kaydedilir) ile verilir.</item>
/// </list>
/// </summary>
public sealed class CreateInvoiceRequest
{
    /// <summary>Faturalanacak işler. Hepsi aynı müşteriye ait olmalıdır. Serbest faturada boş bırakılır.</summary>
    public List<Guid> JobIds { get; set; } = [];
    /// <summary>Serbest fatura: kayıtlı müşteri.</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>Serbest fatura: kayıtlı olmayan müşteri. Fatura ile aynı işlemde müşteri olarak kaydedilir.</summary>
    public CustomerRequest? NewCustomer { get; set; }
    /// <summary>Serbest fatura kalemleri.</summary>
    public List<InvoiceLineRequest> Lines { get; set; } = [];
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
    /// <summary>true: faturaya tek kalem "Nakliye Bedeli" yazılır; işler yalnızca iş dökümünde ayrı ayrı yer alır.</summary>
    public bool SingleLine { get; set; }
}

public sealed class UpdateInvoiceRequest
{
    /// <summary>İşlerden kesilen faturanın işleri. Serbest faturada boş bırakılır.</summary>
    public List<Guid> JobIds { get; set; } = [];
    /// <summary>Serbest faturanın kalemleri (işlerden kesilen faturada boş bırakılır).</summary>
    public List<InvoiceLineRequest> Lines { get; set; } = [];
    public DateOnly InvoiceDate { get; set; }
    public decimal VatRate { get; set; }
    public string? Notes { get; set; }
    /// <summary>KDV tevkifat kodu. Boş: tevkifat yok.</summary>
    public string? WithholdingCode { get; set; }
    /// <summary>Faturada yazılacak IBAN. Boşsa App:CompanyIban kullanılır.</summary>
    public string? Iban { get; set; }
    /// <summary>true: faturaya tek kalem "Nakliye Bedeli" yazılır; işler yalnızca iş dökümünde ayrı ayrı yer alır.</summary>
    public bool SingleLine { get; set; }
}

public sealed class CancelInvoiceRequest
{
    public string? Reason { get; set; }
}

public sealed class InvoiceQuery : PagedQuery
{
    public Guid? CustomerId { get; set; }
    /// <summary>Birden fazla durum için ?status=Draft&amp;status=Pending</summary>
    public List<InvoiceStatus>? Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Search { get; set; }
}

public sealed record OfficialInvoicePdf(byte[] Content, bool FromProvider);

public sealed record FileDownload(byte[] Content, string FileName, string ContentType = "application/pdf");
