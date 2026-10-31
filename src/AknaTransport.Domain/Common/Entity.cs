namespace AknaTransport.Domain.Common;

/// <summary>Tüm varlıkların ortak kimliği. Sıralı (v7) Guid indeks yerleşimini iyileştirir.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}

/// <summary>CreatedAt / UpdatedAt alanları DbContext tarafından UTC olarak doldurulur.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
}
