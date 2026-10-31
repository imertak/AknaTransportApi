using AknaTransport.Api.Infrastructure;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Auth;
using AknaTransport.Business.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AknaTransport.Api.Controllers;

/// <summary>Kimlik doğrulama. Açık kayıt yoktur; kullanıcıları yalnızca yöneticiler ekler.</summary>
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, IUserService userService) : ApiControllerBase
{
    /// <summary>E-posta ve şifre ile giriş; access ve refresh token döner.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<BaseResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct) =>
        Success(await authService.LoginAsync(request, ct), "Giriş başarılı.");

    /// <summary>
    /// Yönetici yeni kullanıcı (şoför veya yönetici) ekler. Şifre sistem tarafından rastgele üretilir
    /// ve kullanıcının telefonuna SMS ile gönderilir. Kullanıcı ilk girişte şifresini değiştirmek zorundadır.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<BaseResponse<UserCredentialResult>>> Register(CreateUserRequest request, CancellationToken ct)
    {
        var result = await userService.CreateAsync(request, ct);
        return Created(nameof(UsersController.Get), new { controller = "Users", id = result.User.Id }, result, result.Message);
    }

    /// <summary>Refresh token ile yeni token çifti alır (token rotation).</summary>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<BaseResponse<AuthResponse>>> RefreshToken(RefreshTokenRequest request, CancellationToken ct) =>
        Success(await authService.RefreshAsync(request, ct));

    /// <summary>Refresh token'ı iptal eder. AllDevices=true ise tüm oturumlar kapatılır.</summary>
    [HttpPost("logout")]
    [Authorize]
    [AllowWhenPasswordChangeRequired]
    public async Task<ActionResult<BaseResponse>> Logout(LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request, ct);
        return Done("Çıkış yapıldı.");
    }

    /// <summary>Şifre sıfırlama e-postası gönderir. Kullanıcı varlığı sızdırılmaz; yanıt her zaman aynıdır.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<BaseResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await authService.ForgotPasswordAsync(request, ct);
        return Done("Bu e-posta adresi kayıtlıysa şifre sıfırlama bağlantısı gönderildi.");
    }

    /// <summary>E-postadaki kod ile yeni şifre belirler; tüm oturumlar kapatılır.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<BaseResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await authService.ResetPasswordAsync(request, ct);
        return Done("Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz.");
    }

    /// <summary>Oturumdaki kullanıcının şifresini değiştirir ve yeni token çifti döner (geçici şifre için zorunlu adım).</summary>
    [HttpPost("change-password")]
    [Authorize]
    [AllowWhenPasswordChangeRequired]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<BaseResponse<AuthResponse>>> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        Success(await authService.ChangePasswordAsync(request, ct), "Şifreniz değiştirildi.");

    /// <summary>Oturumdaki kullanıcının bilgileri.</summary>
    [HttpGet("me")]
    [Authorize]
    [AllowWhenPasswordChangeRequired]
    public async Task<ActionResult<BaseResponse<UserDto>>> Me(CancellationToken ct) =>
        Success(await authService.GetCurrentUserAsync(ct));
}
