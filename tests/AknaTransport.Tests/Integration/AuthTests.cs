using System.Net;
using AknaTransport.Business.Features.Auth;
using AknaTransport.Business.Features.Jobs;
using AknaTransport.Business.Features.Users;
using AknaTransport.Domain.Enums;
using AknaTransport.Tests.Infrastructure;

namespace AknaTransport.Tests.Integration;

public class AuthTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Admin_can_login()
    {
        var auth = await _client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword);
        Assert.Equal(UserRole.Admin, auth.User.Role);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));

        var me = await _client.OkAsync<UserDto>(HttpMethod.Get, "/api/auth/me", token: auth.AccessToken);
        Assert.Equal(AknaApiFactory.AdminEmail, me.Email);
    }

    [Fact]
    public async Task Driver_can_login_after_admin_creates_account()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);

        var auth = await _client.LoginAsync(driver.Email, driver.Password);
        Assert.Equal(UserRole.User, auth.User.Role);
        Assert.False(auth.User.MustChangePassword);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var (status, body) = await _client.SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/login",
            new LoginRequest { Email = AknaApiFactory.AdminEmail, Password = "yanlis-sifre-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.False(body!.IsSuccess);
    }

    [Fact]
    public async Task Self_registration_is_not_allowed()
    {
        var request = new CreateUserRequest { FullName = "Kendi Kendine", Email = "self@test.local", PhoneNumber = "05550000000" };
        var (anonymous, _) = await _client.SendAsync<object>(HttpMethod.Post, "/api/auth/register", request);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous);

        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var (asDriver, _) = await _client.SendAsync<object>(HttpMethod.Post, "/api/auth/register", request, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, asDriver);
    }

    [Fact]
    public async Task Temporary_password_must_be_changed_before_using_api()
    {
        var admin = await _client.AdminTokenAsync();
        var created = await _client.OkAsync<UserCredentialResult>(HttpMethod.Post, "/api/users",
            new CreateUserRequest { FullName = "Geçici", Email = $"tmp-{Guid.NewGuid():N}@test.local", PhoneNumber = "05551110000" }, admin);
        Assert.True(created.User.MustChangePassword);
        Assert.Matches("^[A-Za-z0-9]{10}$", created.TemporaryPassword!);

        var login = await _client.LoginAsync(created.User.Email, created.TemporaryPassword!);
        Assert.True(login.User.MustChangePassword);

        var (blocked, _) = await _client.SendAsync<object>(HttpMethod.Get, "/api/jobs/my-jobs", token: login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, blocked);
        await _client.OkAsync<UserDto>(HttpMethod.Get, "/api/auth/me", token: login.AccessToken);

        var changed = await _client.OkAsync<AuthResponse>(HttpMethod.Post, "/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = created.TemporaryPassword!, NewPassword = "YeniSifre2026" }, login.AccessToken);
        await _client.OkAsync<object>(HttpMethod.Get, "/api/jobs/my-jobs", token: changed.AccessToken);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_is_rejected()
    {
        var auth = await _client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword);
        var refreshed = await _client.OkAsync<AuthResponse>(HttpMethod.Post, "/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.NotEqual(auth.RefreshToken, refreshed.RefreshToken);

        var (reuse, _) = await _client.SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse);

        // Tekrar kullanım tespit edildiği için yeni token da iptal edilmiştir.
        var (afterTheft, _) = await _client.SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = refreshed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var auth = await _client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword);
        await _client.OkAsync<object>(HttpMethod.Post, "/api/auth/logout", new LogoutRequest { RefreshToken = auth.RefreshToken }, auth.AccessToken);
        var (status, _) = await _client.SendAsync<AuthResponse>(HttpMethod.Post, "/api/auth/refresh-token", new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Unauthenticated_request_gets_standard_401_response()
    {
        var (status, body) = await _client.SendAsync<object>(HttpMethod.Get, "/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.False(body!.IsSuccess);
        Assert.False(string.IsNullOrEmpty(body.Message));
    }

    [Fact]
    public async Task Forgot_password_queues_email_without_revealing_user()
    {
        var (known, a) = await _client.SendAsync<object>(HttpMethod.Post, "/api/auth/forgot-password", new ForgotPasswordRequest { Email = AknaApiFactory.AdminEmail });
        var (unknown, b) = await _client.SendAsync<object>(HttpMethod.Post, "/api/auth/forgot-password", new ForgotPasswordRequest { Email = "yok@test.local" });
        Assert.Equal(HttpStatusCode.OK, known);
        Assert.Equal(HttpStatusCode.OK, unknown);
        Assert.Equal(a!.Message, b!.Message);
    }
}
