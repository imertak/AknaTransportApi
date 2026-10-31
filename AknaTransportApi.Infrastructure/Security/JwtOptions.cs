namespace AknaTransportApi.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>En az 32 karakter. Environment variable: Jwt__Secret</summary>
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AknaTransport";
    public string Audience { get; set; } = "AknaTransport.Mobile";
    public int AccessTokenExpirationMinutes { get; set; } = 30;
    public int RefreshTokenExpirationDays { get; set; } = 30;
}

public static class AppClaimTypes
{
    /// <summary>Geçici şifreyle giriş yapan kullanıcıların token'ında bulunur.</summary>
    public const string MustChangePassword = "must_change_password";
}
