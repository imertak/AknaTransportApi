using AknaTransport.Api.Infrastructure;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransport.Api.Controllers;

/// <summary>Kullanıcı (şoför / yönetici) yönetimi.</summary>
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController(IUserService userService) : ApiControllerBase
{
    /// <summary>Kullanıcıları listeler. Şoför seçimi için ?role=User&amp;isActive=true</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<UserDto>>>> List([FromQuery] UserQuery query, CancellationToken ct) =>
        Success(await userService.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BaseResponse<UserDto>>> Get(Guid id, CancellationToken ct) =>
        Success(await userService.GetAsync(id, ct));

    /// <summary>Kullanıcı ekler (POST /api/auth/register ile aynı). Geçici şifre SMS ile gönderilir.</summary>
    [HttpPost]
    public async Task<ActionResult<BaseResponse<UserCredentialResult>>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var result = await userService.CreateAsync(request, ct);
        return Created(nameof(Get), new { id = result.User.Id }, result, result.Message);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BaseResponse<UserDto>>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        Success(await userService.UpdateAsync(id, request, ct), "Kullanıcı güncellendi.");

    /// <summary>Yeni geçici şifre üretir, SMS ile gönderir ve kullanıcının tüm oturumlarını kapatır.</summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<ActionResult<BaseResponse<UserCredentialResult>>> ResetPassword(Guid id, CancellationToken ct)
    {
        var result = await userService.ResetPasswordAsync(id, ct);
        return Success(result, result.Message);
    }
}
