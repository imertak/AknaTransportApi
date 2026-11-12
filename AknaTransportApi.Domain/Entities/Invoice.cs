
namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Fatura. Draft faturalar resmi geçerliliğe sahip değildir; Issued durumuna yalnızca
/// gerçek e-Arşiv sağlayıcısından başarılı yanıt alındığında geçilir.
/// </summary>
public class Invoice : Entity, IHasTimestamps
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal SubTotal { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal GrandTotal { get; private set; }
    /// <summary>KDV tevkifat kodu (ör. 624 Yük taşımacılığı). Boşsa tevkifat yoktur.</summary>
    public string? WithholdingCode { get; private set; }
    /// <summary>KDV'nin alıcı tarafından kesilen yüzdesi (2/10 → 20).</summary>
    public decimal WithholdingRate { get; private set; }
    public decimal WithholdingAmount { get; private set; }
    /// <summary>Alıcının ödeyeceği tutar: genel toplam − tevkifat.</summary>
    public decimal PayableAmount { get; private set; }
    /// <summary>Faturada yazılan IBAN (oluşturma anındaki değer korunur).</summary>
    public string? Iban { get; set; }
    public string Currency { get; set; } = Money.DefaultCurrency;
    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;
    public string? Notes { get; set; }
    /// <summary>
    /// true: fatura işleri tek tek yazmadan tek kalemde ("Nakliye Bedeli") kesilir. İşler yine kalem olarak
    /// saklanır ve iş dökümünde ayrı ayrı yer alır; yalnızca faturaya (e-Arşiv ve PDF) yazılan satırlar değişir.
    /// </summary>
    public bool SingleLine { get; set; }

    /// <summary>Sağlayıcının döndürdüğü belge kimliği (ETTN/UUID).</summary>
    public string? EArchiveReference { get; private set; }
    /// <summary>Sağlayıcının verdiği resmi fatura numarası (ör. GİB formatı).</summary>
    public string? EArchiveInvoiceNumber { get; private set; }
    public string? EArchiveInvoiceUrl { get; private set; }
    public string? EArchiveProvider { get; private set; }
    /// <summary>Sağlayıcıdan alınan resmi PDF'in depolama yolu (göreli).</summary>
    public string? InvoicePdfPath { get; set; }
    public string? LastError { get; private set; }

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime? IssuedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();

    public bool IsEditable => Status is InvoiceStatus.Draft or InvoiceStatus.Failed;

    /// <summary>
    /// true: fatura işlerden değil, elle girilen kalemlerden oluşur (işe bağlı olmayan serbest fatura).
    /// Kalemlerin yüklenmiş olması gerekir.
    /// </summary>
    public bool IsFreeForm => Items.Count > 0 && Items.All(x => x.JobId is null);

    /// <summary>Elle yüklenen faturanın sağlayıcı adı: fatura başka bir yerde (ör. başka bir firmaya) kesilmiştir.</summary>
    public const string ManualProvider = "Manuel";

    /// <summary>
    /// true: fatura bu sistemde kesilmedi; başka yerde kesilen PDF elle yüklendi. e-Arşiv referansı yoktur,
    /// resmi PDF olarak yüklenen dosya kullanılır ve iptal sağlayıcıya gönderilmez.
    /// </summary>
    public bool IsManual => EArchiveProvider == ManualProvider;

    /// <summary>Kalemlerden ara toplam, KDV, tevkifat ve ödenecek tutarı yeniden hesaplar.</summary>
    public void Recalculate(decimal vatRate, WithholdingType? withholding = null)
    {
        if (withholding is not null && vatRate <= 0) throw new DomainException("KDV oranı 0 olan faturada tevkifat uygulanamaz.");
        if (vatRate is < 0 or > 100) throw new DomainException("KDV oranı 0 ile 100 arasında olmalıdır.");
        VatRate = vatRate;
        foreach (var item in Items)
        {
            item.VatRate = vatRate;
            item.LineTotal = Money.Round(item.Quantity * item.UnitPrice);
        }
        SubTotal = Money.Round(Items.Sum(i => i.LineTotal));
        VatAmount = Money.CalculateVat(SubTotal, vatRate);
        GrandTotal = SubTotal + VatAmount;
        WithholdingCode = withholding?.Code;
        WithholdingRate = withholding?.Rate ?? 0;
        WithholdingAmount = withholding is null ? 0 : Money.Round(VatAmount * withholding.Rate / 100m);
        PayableAmount = GrandTotal - WithholdingAmount;
        Touch();
    }

    /// <summary>Tek kalemli faturada yazılan kalem adı.</summary>
    public const string SingleLineDescription = "Nakliye Bedeli";

    /// <summary>Faturanın "Yalnız ..." satırı (ödenecek tutarın yazıyla gösterimi).</summary>
    public string AmountInWordsText => AmountInWords.Write(PayableAmount, Currency);

    public void EnsureEditable()
    {
        if (!IsEditable) throw new InvalidInvoiceStateException($"'{Status}' durumundaki fatura düzenlenemez.");
    }

    public void MarkPending(string provider)
    {
        if (!IsEditable) throw new InvalidInvoiceStateException($"'{Status}' durumundaki fatura kesilemez.");
        Status = InvoiceStatus.Pending;
        EArchiveProvider = provider;
        LastError = null;
        Touch();
    }

    public void SetProviderReference(string? reference, string? invoiceNumber, string? url)
    {
        EArchiveReference = reference ?? EArchiveReference;
        EArchiveInvoiceNumber = invoiceNumber ?? EArchiveInvoiceNumber;
        EArchiveInvoiceUrl = url ?? EArchiveInvoiceUrl;
        Touch();
    }

    /// <summary>Gerçek sağlayıcı yanıtıyla faturayı kesilmiş olarak işaretler.</summary>
    public void MarkIssued(string reference, string? invoiceNumber, string? url, DateTime now)
    {
        if (Status != InvoiceStatus.Pending) throw new InvalidInvoiceStateException("Yalnızca sağlayıcıya gönderilmiş fatura kesildi olarak işaretlenebilir.");
        if (string.IsNullOrWhiteSpace(reference)) throw new InvalidInvoiceStateException("e-Arşiv referansı olmadan fatura kesildi olarak işaretlenemez.");
        Status = InvoiceStatus.Issued;
        SetProviderReference(reference, invoiceNumber, url);
        IssuedAt = now;
        LastError = null;
    }

    /// <summary>
    /// Fatura başka yerde kesildi ve PDF'i elle yüklendi: taslak (veya başarısız) fatura kesilmiş sayılır.
    /// Zaten elle yüklenmiş faturada yalnızca fatura numarası güncellenir (dosya değiştirme).
    /// </summary>
    public void MarkIssuedManually(string? invoiceNumber, DateTime now)
    {
        if (Status == InvoiceStatus.Issued && IsManual)
        {
            EArchiveInvoiceNumber = invoiceNumber ?? EArchiveInvoiceNumber;
            Touch();
            return;
        }
        if (!IsEditable) throw new InvalidInvoiceStateException($"'{Status}' durumundaki faturaya elle fatura yüklenemez.");
        Status = InvoiceStatus.Issued;
        EArchiveProvider = ManualProvider;
        EArchiveReference = null;
        EArchiveInvoiceUrl = null;
        EArchiveInvoiceNumber = invoiceNumber;
        IssuedAt = now;
        LastError = null;
        Touch();
    }

    /// <summary>
    /// Sağlayıcıdaki imzalanmamış taslak silindiğinde fatura yeniden düzenlenebilir taslağa döner.
    /// Kesilmiş (imzalı) faturaya uygulanamaz.
    /// </summary>
    public void RevertToDraft()
    {
        if (Status != InvoiceStatus.Pending) throw new InvalidInvoiceStateException("Yalnızca sağlayıcıda bekleyen fatura taslağa döndürülebilir.");
        Status = InvoiceStatus.Draft;
        EArchiveReference = null;
        EArchiveInvoiceNumber = null;
        EArchiveInvoiceUrl = null;
        LastError = null;
        Touch();
    }

    public void MarkFailed(string error)
    {
        if (Status != InvoiceStatus.Pending) throw new InvalidInvoiceStateException("Yalnızca gönderimdeki fatura başarısız olarak işaretlenebilir.");
        Status = InvoiceStatus.Failed;
        LastError = error.Length > 2000 ? error[..2000] : error;
        Touch();
    }

    public void Cancel(DateTime now)
    {
        if (Status is InvoiceStatus.Cancelled) throw new InvalidInvoiceStateException("Fatura zaten iptal edilmiş.");
        if (Status is InvoiceStatus.Pending) throw new InvalidInvoiceStateException("Sağlayıcı yanıtı beklenen fatura iptal edilemez.");
        Status = InvoiceStatus.Cancelled;
        CancelledAt = now;
        Touch();
    }

    /// <summary>Yalnızca iptal edilmiş fatura kalıcı olarak silinebilir.</summary>
    public void EnsureDeletable()
    {
        if (Status != InvoiceStatus.Cancelled) throw new InvalidInvoiceStateException("Yalnızca iptal edilmiş fatura silinebilir. Önce faturayı iptal edin.");
    }

    private void Touch() => ConcurrencyStamp = Guid.NewGuid();
}

public class InvoiceItem : Entity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    /// <summary>Kalemin geldiği iş. Boş: işe bağlı olmayan serbest fatura kalemi (açıklama, miktar ve fiyat elle girilir).</summary>
    public Guid? JobId { get; set; }
    public TransportJob? Job { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal LineTotal { get; set; }
}
