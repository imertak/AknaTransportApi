using AknaTransport.Domain.Common;

namespace AknaTransport.Domain.Entities;

/// <summary>
/// Refresh token kaydı. Token'ın kendisi değil SHA-256 özeti saklanır.
/// Her yenilemede token döndürülür (rotation); iptal edilmiş bir token tekrar kullanılırsa
/// kullanıcının tüm oturumları kapatılır.
/// </summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, string reason, Guid? replacedBy = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedReason = reason;
        ReplacedByTokenId = replacedBy;
    }
}
