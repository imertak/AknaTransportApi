namespace AknaTransportApi.Domain.Entities;

/// <summary>
/// İşin yetkili kişisi (yük sahibi, teslim alan vb.). Bir işte birden fazla olabilir;
/// şoför bu numaralara iş detayından ulaşır.
/// </summary>
public class TransportJobContact : Entity
{
    public const int RoleMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int MaxPerJob = 10;

    public Guid TransportJobId { get; set; }
    public TransportJob? TransportJob { get; set; }
    /// <summary>Serbest metin görev: "Yük sahibi", "Teslim alan" gibi.</summary>
    public string? Role { get; set; }
    public string? Name { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Yeni yetkili kişi bilgisi (iş oluşturma / güncelleme).</summary>
public sealed record JobContactInput(string? Role, string? Name, string PhoneNumber);
