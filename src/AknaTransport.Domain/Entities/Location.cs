using AknaTransport.Domain.Common;

namespace AknaTransport.Domain.Entities;

/// <summary>Konum geçmişi kaydı. Yalnızca filtreden geçen (anlamlı) konumlar yazılır.</summary>
public class Location : Entity
{
    public Guid JobId { get; set; }
    public TransportJob? Job { get; set; }
    public Guid DriverId { get; set; }
    public User? Driver { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public DateTime RecordedAt { get; set; }
}

/// <summary>
/// İş başına tek satırlık son konum tablosu. Harita ve canlı takip sorguları
/// büyük geçmiş tablosuna gitmeden birincil anahtar üzerinden okunur.
/// </summary>
public class JobLatestLocation
{
    public Guid JobId { get; set; }
    public TransportJob? Job { get; set; }
    public Guid DriverId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Müşteriye verilecek salt okunur takip bağlantısı. Token'ın yalnızca özeti saklanır.
/// Kullanıcı rolü gerektirmez; erişim yalnızca ilgili işin konumuyla sınırlıdır.
/// </summary>
public class JobTrackingToken : Entity
{
    public Guid JobId { get; set; }
    public TransportJob? Job { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime? RevokedAt { get; set; }
}
