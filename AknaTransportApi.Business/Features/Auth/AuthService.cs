using System.Security.Cryptography;
using System.Text;
using AknaTransport.Business.Abstractions;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Outbox;
using AknaTransport.Business.Features.Users;
using AknaTransport.Business.Options;
using AknaTransport.Domain.Entities;
using AknaTransport.Domain.Enums;
using AknaTransport.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransport.Business.Features.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentUserAsync(CancellationToken ct);
}

public sealed class AuthService(
    IRepository<User> users,
    IRepository<RefreshToken> refreshTokens,
    IRepository<OutboxMessage> outbox,
    IRepository<EmailLog> emailLogs,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ICurrentUser currentUser,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidCredentials = "E-posta veya şifre hatalı.";
    // Kullanıcı bulunamadığında da hash doğrulaması yapılarak zamanlama farkı azaltılır.
    private static readonly Lazy<string> DummyHash = new(() => new Pbkdf2Dummy().Hash);

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(request.Email);
        var user = await users.Query().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            passwordHasher.Verify(request.Password, DummyHash.Value);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Başarısız giriş denemesi. UserId: {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive) throw new UnauthorizedException("Hesabınız pasif durumda. Yöneticinizle iletişime geçin.");

        var response = IssueTokens(user);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Kullanıcı giriş yaptı. UserId: {UserId}, Role: {Role}", user.Id, user.Role);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct)
    {
        var now = Now;
        var hash = tokenService.HashToken(request.RefreshToken);
        var token = await refreshTokens.Query().Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                    ?? throw new UnauthorizedException("Oturum geçersiz. Lütfen tekrar giriş yapın.");

        if (token.RevokedAt is not null)
        {
            // Döndürülmüş (iptal edilmiş) bir token'ın tekrar kullanılması token hırsızlığına işaret eder.
            if (token.ReplacedByTokenId is not null)
            {
                logger.LogWarning("İptal edilmiş refresh token tekrar kullanıldı; tüm oturumlar kapatılıyor. UserId: {UserId}", token.UserId);
                await RevokeAllAsync(token.UserId, now, "Token tekrar kullanımı tespit edildi", ct);
                await unitOfWork.SaveChangesAsync(ct);
            }
            throw new UnauthorizedException("Oturum geçersiz. Lütfen tekrar giriş yapın.");
        }

        if (token.ExpiresAt <= now) throw new UnauthorizedException("Oturum süresi doldu. Lütfen tekrar giriş yapın.");
        if (token.User is not { IsActive: true } user) throw new UnauthorizedException("Hesabınız pasif durumda.");

        var response = IssueTokens(user, out var newToken);
        token.Revoke(now, "Yenilendi", newToken.Id);
        await unitOfWork.SaveChangesAsync(ct); // RefreshToken satırındaki eşzamanlı yenileme 409 ile sonuçlanır.
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var now = Now;
        if (request.AllDevices)
        {
            await RevokeAllAsync(currentUser.UserId, now, "Tüm cihazlardan çıkış", ct);
        }
        else if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var hash = tokenService.HashToken(request.RefreshToken);
            var token = await refreshTokens.Query().FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == currentUser.UserId, ct);
            token?.Revoke(now, "Çıkış yapıldı");
        }
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(request.Email);
        var user = await users.Query().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.IsActive, ct);
        if (user is null)
        {
            // Kullanıcı varlığı dışarıya sızdırılmaz.
            logger.LogInformation("Şifre sıfırlama talebi: eşleşen aktif kullanıcı yok.");
            return;
        }

        var now = Now;
        var token = tokenService.GenerateSecureToken();
        var expiresAt = now.AddMinutes(appOptions.Value.PasswordResetTokenMinutes);
        user.PasswordResetTokenHash = tokenService.HashToken(token);
        user.PasswordResetTokenExpiresAt = expiresAt;

        var log = new EmailLog
        {
            Type = EmailType.PasswordReset,
            RecipientEmail = user.Email,
            Subject = EmailTemplates.PasswordResetSubject,
            Status = EmailStatus.Pending,
            CreatedAt = now,
        };
        emailLogs.Add(log);
        OutboxWriter.Enqueue(outbox, OutboxMessageTypes.PasswordResetEmail,
            new PasswordResetEmailPayload(user.Id, log.Id, user.Email, user.FullName, token, expiresAt), now);

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        const string invalid = "Sıfırlama bağlantısı geçersiz veya süresi dolmuş.";
        var now = Now;
        var normalized = User.NormalizeEmail(request.Email);
        var user = await users.Query().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null || user.PasswordResetTokenHash is null || user.PasswordResetTokenExpiresAt is null || user.PasswordResetTokenExpiresAt <= now)
            throw new BusinessValidationException(invalid);

        var providedHash = tokenService.HashToken(request.Token);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedHash), Encoding.UTF8.GetBytes(user.PasswordResetTokenHash)))
            throw new BusinessValidationException(invalid);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.MustChangePassword = false;
        await RevokeAllAsync(user.Id, now, "Şifre sıfırlandı", ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Şifreyi değiştirir, diğer oturumları kapatır ve yeni token çifti döndürür.</summary>
    public async Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(currentUser.UserId, ct) ?? throw NotFoundException.For("Kullanıcı");
        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessValidationException("Mevcut şifre hatalı.", [new ErrorDetail(nameof(request.CurrentPassword), "Mevcut şifre hatalı.")]);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        await RevokeAllAsync(user.Id, Now, "Şifre değiştirildi", ct);
        var response = IssueTokens(user);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct)
    {
        return await users.QueryAsNoTracking().Where(u => u.Id == currentUser.UserId && u.IsActive)
                   .Select(UserMappings.Projection).FirstOrDefaultAsync(ct)
               ?? throw new UnauthorizedException("Oturum geçersiz.");
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private AuthResponse IssueTokens(User user) => IssueTokens(user, out _);

    private AuthResponse IssueTokens(User user, out RefreshToken refreshToken)
    {
        var now = Now;
        var access = tokenService.CreateAccessToken(user);
        var raw = tokenService.GenerateSecureToken();
        refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = tokenService.HashToken(raw),
            CreatedAt = now,
            ExpiresAt = now.Add(tokenService.RefreshTokenLifetime),
            CreatedByIp = currentUser.IpAddress,
        };
        refreshTokens.Add(refreshToken);
        return new AuthResponse(access.Token, access.ExpiresAt, raw, refreshToken.ExpiresAt, "Bearer", user.ToDto());
    }

    private async Task RevokeAllAsync(Guid userId, DateTime now, string reason, CancellationToken ct)
    {
        var active = await refreshTokens.Query().Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in active) t.Revoke(now, reason);
    }

    /// <summary>Var olmayan kullanıcı için sabit maliyetli doğrulama hedefi.</summary>
    private sealed class Pbkdf2Dummy
    {
        public string Hash { get; } = "PBKDF2-SHA256$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    }
}
