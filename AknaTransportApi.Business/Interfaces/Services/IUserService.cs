using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserCredentialResult> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserCredentialResult> ResetPasswordAsync(Guid id, CancellationToken ct);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
}
