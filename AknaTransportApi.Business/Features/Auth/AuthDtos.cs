using AknaTransport.Business.Features.Users;
using AknaTransport.Domain.Enums;
using FluentValidation;

namespace AknaTransport.Business.Features.Auth;

public sealed class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class LogoutRequest
{
    public string? RefreshToken { get; set; }
    /// <summary>true ise kullanıcının tüm cihazlardaki oturumları kapatılır.</summary>
    public bool AllDevices { get; set; }
}

public sealed class ForgotPasswordRequest
{
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    string TokenType,
    UserDto User);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta zorunludur.").EmailAddress().WithMessage("Geçerli bir e-posta girin.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Şifre zorunludur.").MaximumLength(128);
    }
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Geçerli bir e-posta girin.");
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Geçerli bir e-posta girin.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Sıfırlama kodu zorunludur.").MaximumLength(512);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Mevcut şifre zorunludur.");
        RuleFor(x => x.NewPassword).StrongPassword()
            .NotEqual(x => x.CurrentPassword).WithMessage("Yeni şifre mevcut şifreden farklı olmalıdır.");
    }
}
