using AknaTransport.Domain.Common;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Domain.Entities;

public class User : Entity, IHasTimestamps
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Benzersizlik kontrolü için küçük harfe çevrilmiş e-posta.</summary>
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;

    /// <summary>Yönetici tarafından üretilen geçici şifreyle oluşturulan hesaplarda ilk girişte şifre değişikliği zorunludur.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Şifre sıfırlama token'ının SHA-256 özeti. Token'ın kendisi saklanmaz.</summary>
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public bool IsAdmin => Role == UserRole.Admin;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
