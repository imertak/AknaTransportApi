using System.Security.Claims;
using AknaTransport.Business.Common;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Api.Infrastructure;

/// <summary>JWT içindeki kullanıcı kimliği ve rolünü okur.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;

    public UserRole Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : UserRole.User;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
