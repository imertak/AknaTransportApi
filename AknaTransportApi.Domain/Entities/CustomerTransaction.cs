namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// Müşteri cari hesabındaki elle girilen hareket (tahsilat, iade, devir borcu). Kesilmiş (Issued) faturalar
/// ayrı bir kayıt gerektirmeden borç olarak sayılır; iptal edilen fatura bakiyeden kendiliğinden düşer.
/// </summary>
public class CustomerTransaction : Entity, IHasTimestamps
{
    public const int MethodMaxLength = 40;
    public const int DescriptionMaxLength = 500;

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public CustomerTransactionType Type { get; set; }
    /// <summary>Her zaman pozitif; yönü <see cref="Type"/> belirler.</summary>
    public decimal Amount { get; set; }
    public DateOnly TransactionDate { get; set; }
    /// <summary>Ödeme şekli (Havale/EFT, Nakit, Çek, Kredi kartı...).</summary>
    public string? Method { get; set; }
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Bakiyeye etkisi: tahsilat eksi, iade ve borç kaydı artı.</summary>
    public decimal SignedAmount => Type == CustomerTransactionType.Collection ? -Amount : Amount;
}
