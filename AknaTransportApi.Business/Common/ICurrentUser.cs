using AknaTransport.Domain.Enums;

namespace AknaTransport.Business.Common;

/// <summary>İsteği yapan kullanıcının JWT'den okunan bilgileri.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    UserRole Role { get; }
    bool IsAdmin => Role == UserRole.Admin;
    string? IpAddress { get; }
}
