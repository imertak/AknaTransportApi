namespace AknaTransportApi.Business.Dtos.Offers;

/// <summary>Teklif listesi satırı.</summary>
public sealed record TransportOfferListItemDto(
    Guid Id,
    string OfferNumber,
    DateOnly OfferDate,
    DateOnly ValidUntil,
    Guid CustomerId,
    string CustomerName,
    string LoadingPoint,
    string UnloadingPoint,
    TransportType TransportType,
    string VehicleType,
    decimal TotalAmount,
    string Currency,
    /// <summary>Ekranda gösterilecek durum (süresi geçmiş taslak/bekleyen teklif Expired döner).</summary>
    TransportOfferStatus Status,
    Guid? ConvertedJobId,
    DateTime CreatedAt);

public sealed record TransportOfferItemDto(Guid Id, int Sequence, string Description, string? Unit, decimal Quantity, decimal UnitPrice, decimal TotalPrice);

public sealed record TransportOfferHistoryDto(
    Guid Id,
    TransportOfferStatus? OldStatus,
    TransportOfferStatus NewStatus,
    string? Description,
    Guid ChangedByUserId,
    string? ChangedByUserName,
    DateTime CreatedAt);

/// <summary>Teklif detayı. Tutarların tamamı sunucuda hesaplanır.</summary>
public sealed record TransportOfferDetailDto
{
    public Guid Id { get; init; }
    public string OfferNumber { get; init; } = string.Empty;
    public DateOnly OfferDate { get; init; }
    public DateOnly ValidUntil { get; init; }
    public TransportOfferStatus Status { get; init; }
    /// <summary>Veritabanındaki durum (Expired hiçbir zaman saklanmaz).</summary>
    public TransportOfferStatus StoredStatus { get; init; }

    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string? ContactPerson { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerAddress { get; init; }

    public string LoadingPoint { get; init; } = string.Empty;
    public string? LoadingAddress { get; init; }
    public string UnloadingPoint { get; init; } = string.Empty;
    public string? UnloadingAddress { get; init; }
    public TransportType TransportType { get; init; }
    public string VehicleType { get; init; } = string.Empty;
    /// <summary>Araç tipinin adı (ör. "Tenteli").</summary>
    public string VehicleTypeName { get; init; } = string.Empty;
    public string? CargoDescription { get; init; }
    public DateOnly? LoadingDate { get; init; }
    public DateOnly? DeliveryDate { get; init; }
    public decimal? DistanceKm { get; init; }

    public IReadOnlyList<TransportOfferItemDto> Items { get; init; } = [];
    public decimal SubTotal { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal VatRate { get; init; }
    public decimal VatAmount { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = Money.DefaultCurrency;

    public string? PaymentTerms { get; init; }
    public string? DeliveryTerms { get; init; }
    public string? CustomerNotes { get; init; }
    public string? InternalNotes { get; init; }

    public DateTime? SentAt { get; init; }
    public DateTime? AcceptedAt { get; init; }
    public DateTime? RejectedAt { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CancellationReason { get; init; }

    public Guid? ConvertedJobId { get; init; }
    public string? ConvertedJobNumber { get; init; }
    public DateTime? ConvertedAt { get; init; }
    public Guid? CopiedFromOfferId { get; init; }

    public Guid CreatedByUserId { get; init; }
    public string? CreatedByUserName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Fiyat ve taşıma bilgileri düzenlenebilir mi (taslak / yanıt bekleyen / süresi dolmuş).</summary>
    public bool CanEdit { get; init; }
    public bool CanSend { get; init; }
    public bool CanAccept { get; init; }
    public bool CanReject { get; init; }
    public bool CanCancel { get; init; }
    public bool CanConvert { get; init; }
    public bool CanDelete { get; init; }
    /// <summary>PDF dosya adı: AKNA_Teklif_AKT-2026-000001.pdf</summary>
    public string PdfFileName { get; init; } = string.Empty;

    public IReadOnlyList<TransportOfferHistoryDto> History { get; init; } = [];
}

/// <summary>Liste üstündeki istatistik kartları (arama/tarih filtresine uyan teklifler).</summary>
public sealed record TransportOfferStatsDto(
    int Total,
    int Draft,
    int Pending,
    int Accepted,
    int Rejected,
    int Expired,
    int Cancelled,
    /// <summary>Kabul edilen tekliflerin genel toplamı.</summary>
    decimal AcceptedAmount,
    /// <summary>Yanıt bekleyen (süresi dolmamış) tekliflerin genel toplamı.</summary>
    decimal PendingAmount);

/// <summary>İşe dönüştürme sonucu.</summary>
public sealed record ConvertOfferResultDto(TransportOfferDetailDto Offer, Guid JobId, string JobNumber);

/// <summary>Teklifin müşteri bilgileri (teklif anındaki haliyle saklanır).</summary>
public sealed class OfferCustomerRequest
{
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}

public sealed class TransportOfferItemRequest
{
    public string Description { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Teklif oluşturma / güncelleme. Ara toplam, KDV ve genel toplam istekte yoktur; sunucu kalemlerden hesaplar.
/// </summary>
public sealed class TransportOfferRequest
{
    /// <summary>Kayıtlı müşteri. Boşsa <see cref="Customer"/> bilgileriyle yeni müşteri kaydı açılır.</summary>
    public Guid? CustomerId { get; set; }
    public OfferCustomerRequest Customer { get; set; } = new();

    public DateOnly? OfferDate { get; set; }
    /// <summary>Son geçerlilik günü. Boşsa teklif tarihinden itibaren 7 gün.</summary>
    public DateOnly? ValidUntil { get; set; }

    public string LoadingPoint { get; set; } = string.Empty;
    public string? LoadingAddress { get; set; }
    public string UnloadingPoint { get; set; } = string.Empty;
    public string? UnloadingAddress { get; set; }
    public TransportType TransportType { get; set; } = TransportType.Full;
    public string VehicleType { get; set; } = string.Empty;
    public string? CargoDescription { get; set; }
    public DateOnly? LoadingDate { get; set; }
    public DateOnly? DeliveryDate { get; set; }
    public decimal? DistanceKm { get; set; }

    public List<TransportOfferItemRequest> Items { get; set; } = [];
    public decimal DiscountAmount { get; set; }
    public decimal? VatRate { get; set; }
    /// <summary>Varsayılan TRY.</summary>
    public string? Currency { get; set; }

    public string? PaymentTerms { get; set; }
    public string? DeliveryTerms { get; set; }
    public string? CustomerNotes { get; set; }
    public string? InternalNotes { get; set; }
}

public sealed class OfferStatusRequest
{
    /// <summary>Kabulde not, retteki neden (isteğe bağlı), iptalde neden (zorunlu).</summary>
    public string? Reason { get; set; }
}

public sealed class TransportOfferQuery : PagedQuery
{
    /// <summary>Teklif numarası, müşteri adı, yükleme/boşaltma noktasında arama.</summary>
    public string? Search { get; set; }
    public Guid? CustomerId { get; set; }
    /// <summary>Birden fazla durum: ?status=Pending&amp;status=Accepted. Expired, süresi geçmiş taslak/bekleyen teklifleri döndürür.</summary>
    public List<TransportOfferStatus>? Status { get; set; }
    /// <summary>Teklif tarihine göre başlangıç (dahil).</summary>
    public DateOnly? StartDate { get; set; }
    /// <summary>Teklif tarihine göre bitiş (dahil).</summary>
    public DateOnly? EndDate { get; set; }
}

/// <summary>PDF üretimi için teklif belgesi.</summary>
public sealed record TransportOfferDocument
{
    public required string OfferNumber { get; init; }
    public required DateOnly OfferDate { get; init; }
    public required DateOnly ValidUntil { get; init; }
    public required string StatusLabel { get; init; }
    public required bool ShowStatus { get; init; }

    public required string CustomerName { get; init; }
    public string? ContactPerson { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerAddress { get; init; }

    public required string LoadingPoint { get; init; }
    public string? LoadingAddress { get; init; }
    public required string UnloadingPoint { get; init; }
    public string? UnloadingAddress { get; init; }
    public required string TransportTypeLabel { get; init; }
    public required string VehicleTypeName { get; init; }
    public string? CargoDescription { get; init; }
    public DateOnly? LoadingDate { get; init; }
    public DateOnly? DeliveryDate { get; init; }
    public decimal? DistanceKm { get; init; }

    public required IReadOnlyList<TransportOfferItemDto> Items { get; init; }
    public required decimal SubTotal { get; init; }
    public required decimal DiscountAmount { get; init; }
    public required decimal VatRate { get; init; }
    public required decimal VatAmount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public string? AmountInWords { get; init; }

    public string? PaymentTerms { get; init; }
    public string? DeliveryTerms { get; init; }
    public string? CustomerNotes { get; init; }

    public required string CompanyName { get; init; }
    public string? CompanyAddress { get; init; }
    public string? CompanyPhone { get; init; }
    public string? CompanyEmail { get; init; }
    public string? CompanyWebsite { get; init; }
    public string? CompanyTaxOffice { get; init; }
    public string? CompanyTaxNumber { get; init; }
    public string? CompanyIban { get; init; }
    public string? CompanyBankName { get; init; }
    public string? PreparedBy { get; init; }

    public required DateTime GeneratedAtUtc { get; init; }
}
