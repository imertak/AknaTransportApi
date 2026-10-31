using AknaTransport.Domain.Entities;

namespace AknaTransport.Business.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>Kriptografik olarak güvenli rastgele token üretir (refresh / reset / takip).</summary>
    string GenerateSecureToken();

    /// <summary>Token'ın veritabanında saklanacak SHA-256 özeti.</summary>
    string HashToken(string token);

    TimeSpan RefreshTokenLifetime { get; }
}
