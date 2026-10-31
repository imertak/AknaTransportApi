using System.Linq.Expressions;

namespace AknaTransportApi.Business.Dtos.Users;

public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string PhoneNumber,
    UserRole Role,
    bool IsActive,
    bool MustChangePassword,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed class CreateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Geçici şifre bu numaraya SMS ile gönderilir.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>Varsayılan: User (şoför).</summary>
    public UserRole Role { get; set; } = UserRole.User;
}

public sealed class UpdateUserRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Kullanıcı oluşturma / şifre sıfırlama sonucu. Şifre sistem tarafından rastgele üretilir ve
/// SMS ile gönderilir. SMS sağlayıcısı yapılandırılmamışsa geçici şifre yalnızca bu yanıtta
/// bir kez döner (TemporaryPassword) ve hiçbir yerde saklanmaz/loglanmaz.
/// </summary>
public sealed record UserCredentialResult(UserDto User, bool SmsQueued, string? TemporaryPassword, string Message);

public sealed class UserQuery : PagedQuery
{
    public UserRole? Role { get; set; }
    public bool? IsActive { get; set; }
    public string? Search { get; set; }
}
