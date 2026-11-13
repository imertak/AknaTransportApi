namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Müşteriye verilen nakliye teklifi. Tutarlar yalnızca <see cref="Recalculate"/> ile kalemlerden hesaplanır;
/// istemcinin gönderdiği toplamlara güvenilmez. Durum geçişleri bu sınıftaki metotlarla yapılır ve her geçiş
/// <see cref="TransportOfferHistory"/> kaydı üretir. Müşteri bilgileri teklif anındaki haliyle saklanır
/// (müşteri kaydı sonradan değişse de PDF ve teklif değişmez).
/// </summary>
public class TransportOffer : Entity, IHasTimestamps
{
    public const int PointMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int TextMaxLength = 2000;
    public const int MaxItems = 30;
    public const int DefaultValidityDays = 7;

    /// <summary>Düzenlenebilen (fiyat ve taşıma bilgileri değiştirilebilen) durumlar.</summary>
    public static readonly IReadOnlyList<TransportOfferStatus> EditableStatuses = [TransportOfferStatus.Draft, TransportOfferStatus.Pending];

    public string OfferNumber { get; set; } = string.Empty;
    public DateOnly OfferDate { get; set; }
    /// <summary>Teklifin son geçerlilik günü (dahil).</summary>
    public DateOnly ValidUntil { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerAddress { get; set; }

    /// <summary>Yükleme noktası (il / ilçe gibi kısa ad, ör. "İstanbul / Tuzla").</summary>
    public string LoadingPoint { get; set; } = string.Empty;
    /// <summary>Açık yükleme adresi (isteğe bağlı).</summary>
    public string? LoadingAddress { get; set; }
    public string UnloadingPoint { get; set; } = string.Empty;
    public string? UnloadingAddress { get; set; }
    public TransportType TransportType { get; set; }
    /// <summary>Araç tipinin kodu (vehicle_types tablosu, ör. "Tenteli"). İşe dönüştürmede aynen aktarılır.</summary>
    public string VehicleType { get; set; } = string.Empty;
    public string? CargoDescription { get; set; }
    public DateOnly? LoadingDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    /// <summary>Tahmini mesafe (km), isteğe bağlı.</summary>
    public decimal? DistanceKm { get; set; }

    /// <summary>Kalemlerin toplamı (KDV hariç, indirim öncesi).</summary>
    public decimal SubTotal { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal VatAmount { get; private set; }
    /// <summary>Genel toplam: (ara toplam − indirim) + KDV.</summary>
    public decimal TotalAmount { get; private set; }
    public string Currency { get; set; } = Money.DefaultCurrency;

    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    /// <summary>Müşteriye gösterilen notlar (PDF'te yer alır).</summary>
    public string? CustomerNotes { get; set; }
    /// <summary>Özel açıklamalar: firma içi not, PDF'e yazılmaz.</summary>
    public string? InternalNotes { get; set; }

    public TransportOfferStatus Status { get; private set; } = TransportOfferStatus.Draft;
    public DateTime? SentAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Tekliften oluşturulan nakliye işi. Bir tekliften yalnızca bir iş oluşturulabilir.</summary>
    public Guid? ConvertedJobId { get; private set; }
    public TransportJob? ConvertedJob { get; set; }
    public DateTime? ConvertedAt { get; private set; }

    /// <summary>Kopyalanarak oluşturulduysa kaynak teklif.</summary>
    public Guid? CopiedFromOfferId { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>Mantıksal silme: silinen teklif standart sorgulardan çıkar (global query filter), kayıt korunur.</summary>
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedByUserId { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public ICollection<TransportOfferItem> Items { get; set; } = new List<TransportOfferItem>();
    public ICollection<TransportOfferHistory> History { get; set; } = new List<TransportOfferHistory>();

    public bool IsConverted => ConvertedJobId is not null;

    /// <summary>
    /// Ekranda gösterilen durum: geçerlilik tarihi geçmiş taslak veya yanıt bekleyen teklif <see cref="TransportOfferStatus.Expired"/> sayılır.
    /// </summary>
    public TransportOfferStatus EffectiveStatus(DateOnly today) => ResolveStatus(Status, ValidUntil, today);

    public static TransportOfferStatus ResolveStatus(TransportOfferStatus stored, DateOnly validUntil, DateOnly today) =>
        EditableStatuses.Contains(stored) && validUntil < today ? TransportOfferStatus.Expired : stored;

    /// <summary>Yeni teklif: ilk durum kaydını ekler.</summary>
    public void Initialize(Guid actorId, DateTime now, string? note = null)
    {
        Status = TransportOfferStatus.Draft;
        Record(null, Status, actorId, now, note ?? "Teklif oluşturuldu.");
    }

    /// <summary>
    /// Kalemleri yeniden yazar ve tutarları hesaplar. Ara toplam kalemlerden, KDV (ara toplam − indirim) üzerinden
    /// hesaplanır; tüm tutarlar 2 haneye yuvarlanır.
    /// </summary>
    public void SetPricing(IReadOnlyList<TransportOfferItemInput> items, decimal discountAmount, decimal vatRate, DateTime now)
    {
        if (items.Count == 0) throw new DomainException("En az bir fiyat kalemi girilmelidir.");
        if (items.Count > MaxItems) throw new DomainException($"Bir teklifte en fazla {MaxItems} fiyat kalemi olabilir.");
        if (vatRate is < 0 or > 100) throw new DomainException("KDV oranı 0 ile 100 arasında olmalıdır.");
        if (discountAmount < 0) throw new DomainException("İndirim tutarı negatif olamaz.");
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i.Description)) throw new DomainException("Fiyat kalemi açıklaması boş olamaz.");
            if (i.Quantity <= 0) throw new DomainException("Fiyat kalemi miktarı sıfırdan büyük olmalıdır.");
            if (i.UnitPrice < 0) throw new DomainException("Birim fiyat negatif olamaz.");
        }

        Items.Clear();
        var sequence = 1;
        foreach (var i in items)
        {
            var unitPrice = Money.Round(i.UnitPrice);
            Items.Add(new TransportOfferItem
            {
                TransportOfferId = Id,
                Sequence = sequence++,
                Description = i.Description.Trim(),
                Unit = string.IsNullOrWhiteSpace(i.Unit) ? null : i.Unit.Trim(),
                Quantity = i.Quantity,
                UnitPrice = unitPrice,
                TotalPrice = Money.Round(i.Quantity * unitPrice),
                CreatedAt = now,
            });
        }

        var subTotal = Money.Round(Items.Sum(x => x.TotalPrice));
        var discount = Money.Round(discountAmount);
        if (discount > subTotal) throw new DomainException("İndirim tutarı ara toplamdan büyük olamaz.");
        var net = subTotal - discount;
        if (net <= 0) throw new DomainException("Teklif tutarı sıfırdan büyük olmalıdır.");

        SubTotal = subTotal;
        DiscountAmount = discount;
        VatRate = vatRate;
        VatAmount = Money.CalculateVat(net, vatRate);
        TotalAmount = net + VatAmount;
        ConcurrencyStamp = Guid.NewGuid();
    }

    /// <summary>İndirim sonrası, KDV hariç tutar (nakliye işinin fiyatı).</summary>
    public decimal NetAmount => SubTotal - DiscountAmount;

    /// <summary>
    /// Fiyat ve taşıma bilgileri yalnızca taslak veya yanıt bekleyen (süresi dolmuş olanlar dahil) teklifte değişebilir.
    /// Kabul edilen teklif kilitlidir; değişiklik gerekiyorsa teklif kopyalanır.
    /// </summary>
    public void EnsureEditable()
    {
        if (IsDeleted) throw new DomainException("Silinmiş teklif düzenlenemez.");
        if (!EditableStatuses.Contains(Status))
            throw new InvalidOfferStatusTransitionException(Status, "düzenleme",
                Status == TransportOfferStatus.Accepted ? "Kabul edilmiş teklifin fiyat ve taşıma bilgileri değiştirilemez. Değişiklik için teklifi kopyalayın." : null);
    }

    /// <summary>Teklif müşteriye gönderildi: taslak → yanıt bekleniyor. Zaten gönderilmişse false döner.</summary>
    public bool MarkSent(Guid actorId, DateTime now, DateOnly today)
    {
        if (Status == TransportOfferStatus.Pending) return false;
        EnsureStatus(TransportOfferStatus.Draft, "gönderme");
        EnsureNotExpired(today, "gönderilemez");
        var from = Status;
        Status = TransportOfferStatus.Pending;
        SentAt = now;
        Record(from, Status, actorId, now, "Teklif müşteriye gönderildi.");
        return true;
    }

    /// <summary>Müşteri kabul etti. Süresi dolmuş teklif kabul edilemez (önce geçerlilik uzatılmalı).</summary>
    public void Accept(Guid actorId, DateTime now, DateOnly today, string? note)
    {
        if (!EditableStatuses.Contains(Status)) throw new InvalidOfferStatusTransitionException(Status, "kabul");
        EnsureNotExpired(today, "kabul edilemez");
        var from = Status;
        Status = TransportOfferStatus.Accepted;
        AcceptedAt = now;
        Record(from, Status, actorId, now, note is null ? "Teklif müşteri tarafından kabul edildi." : $"Teklif kabul edildi: {note}");
    }

    /// <summary>Müşteri reddetti. Süresi dolmuş teklif de reddedildi olarak kapatılabilir.</summary>
    public void Reject(Guid actorId, DateTime now, string? reason)
    {
        if (!EditableStatuses.Contains(Status)) throw new InvalidOfferStatusTransitionException(Status, "reddetme");
        var from = Status;
        Status = TransportOfferStatus.Rejected;
        RejectedAt = now;
        RejectionReason = reason;
        Record(from, Status, actorId, now, reason is null ? "Teklif müşteri tarafından reddedildi." : $"Teklif reddedildi: {reason}");
    }

    /// <summary>
    /// Teklifi iptal eder (geri çeker). Taslak, yanıt bekleyen ve henüz işe dönüştürülmemiş kabul edilmiş teklifte yapılabilir.
    /// Zaten iptal edilmişse false döner.
    /// </summary>
    public bool Cancel(Guid actorId, DateTime now, string reason)
    {
        if (Status == TransportOfferStatus.Cancelled) return false;
        if (IsConverted) throw new DomainException("Nakliye işine dönüştürülmüş teklif iptal edilemez. Gerekirse işi iptal edin.");
        if (Status == TransportOfferStatus.Rejected) throw new InvalidOfferStatusTransitionException(Status, "iptal");
        var from = Status;
        Status = TransportOfferStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
        Record(from, Status, actorId, now, $"Teklif iptal edildi: {reason}");
        return true;
    }

    /// <summary>Kabul edilmiş teklifi oluşturulan işe bağlar. Aynı tekliften ikinci iş oluşturulamaz.</summary>
    public void MarkConverted(Guid jobId, string jobNumber, Guid actorId, DateTime now)
    {
        EnsureConvertible();
        ConvertedJobId = jobId;
        ConvertedAt = now;
        ConcurrencyStamp = Guid.NewGuid();
        History.Add(new TransportOfferHistory
        {
            TransportOfferId = Id,
            OldStatus = Status,
            NewStatus = Status,
            ChangedByUserId = actorId,
            CreatedAt = now,
            Description = $"Teklif {jobNumber} numaralı nakliye işine dönüştürüldü.",
        });
    }

    public void EnsureConvertible()
    {
        if (IsDeleted) throw new DomainException("Silinmiş teklif nakliye işine dönüştürülemez.");
        if (IsConverted) throw new DomainException("Bu tekliften zaten nakliye işi oluşturuldu.");
        if (Status != TransportOfferStatus.Accepted)
            throw new DomainException("Yalnızca müşteri tarafından kabul edilmiş teklif nakliye işine dönüştürülebilir.");
    }

    /// <summary>Mantıksal silme. İşe dönüştürülmüş teklif silinemez (iş ile bağlantı korunur).</summary>
    public bool SoftDelete(Guid actorId, DateTime now)
    {
        if (IsDeleted) return false;
        if (IsConverted) throw new DomainException("Nakliye işine dönüştürülmüş teklif silinemez.");
        if (Status == TransportOfferStatus.Accepted) throw new DomainException("Kabul edilmiş teklif silinemez. Önce teklifi iptal edin.");
        IsDeleted = true;
        DeletedAt = now;
        DeletedByUserId = actorId;
        ConcurrencyStamp = Guid.NewGuid();
        return true;
    }

    private void EnsureStatus(TransportOfferStatus expected, string action)
    {
        if (Status != expected) throw new InvalidOfferStatusTransitionException(Status, action);
    }

    private void EnsureNotExpired(DateOnly today, string verb)
    {
        if (ValidUntil < today)
            throw new DomainException($"Teklifin geçerlilik süresi {ValidUntil:dd.MM.yyyy} tarihinde doldu; {verb}. Önce teklifi düzenleyip geçerlilik tarihini uzatın.");
    }

    private void Record(TransportOfferStatus? from, TransportOfferStatus to, Guid actorId, DateTime now, string? description)
    {
        ConcurrencyStamp = Guid.NewGuid();
        History.Add(new TransportOfferHistory
        {
            TransportOfferId = Id,
            OldStatus = from,
            NewStatus = to,
            ChangedByUserId = actorId,
            CreatedAt = now,
            Description = description,
        });
    }
}

/// <summary>Teklifin fiyat kalemi (nakliye, yükleme, bekleme bedeli vb.).</summary>
public class TransportOfferItem : Entity
{
    public const int DescriptionMaxLength = 300;
    public const int UnitMaxLength = 20;

    public Guid TransportOfferId { get; set; }
    public TransportOffer? TransportOffer { get; set; }
    public int Sequence { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Birim (Sefer, Saat, Adet …), isteğe bağlı.</summary>
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Teklifin durum geçmişi (oluşturma, gönderme, kabul, ret, iptal, işe dönüştürme).</summary>
public class TransportOfferHistory : Entity
{
    public Guid TransportOfferId { get; set; }
    public TransportOffer? TransportOffer { get; set; }
    /// <summary>İlk oluşturma kaydında null'dır.</summary>
    public TransportOfferStatus? OldStatus { get; set; }
    public TransportOfferStatus NewStatus { get; set; }
    public string? Description { get; set; }
    public Guid ChangedByUserId { get; set; }
    public User? ChangedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Yeni fiyat kalemi bilgisi.</summary>
public sealed record TransportOfferItemInput(string Description, decimal Quantity, decimal UnitPrice, string? Unit = null);
