namespace AknaTransportApi.Business.Interfaces.Services;

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>Kriptografik olarak güvenli rastgele token üretir (refresh / reset / takip).</summary>
    string GenerateSecureToken();

    /// <summary>Token'ın veritabanında saklanacak SHA-256 özeti.</summary>
    string HashToken(string token);

    TimeSpan RefreshTokenLifetime { get; }
}
