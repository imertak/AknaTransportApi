using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AknaTransport.Business.Common;
using AknaTransport.Business.Features.Auth;
using AknaTransport.Business.Features.Customers;
using AknaTransport.Business.Features.Jobs;
using AknaTransport.Business.Features.Users;
using AknaTransport.Domain.Enums;

namespace AknaTransport.Tests.Infrastructure;

public static class Api
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<(HttpStatusCode Status, BaseResponse<T>? Body)> SendAsync<T>(this HttpClient client, HttpMethod method, string url, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var parsed = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<BaseResponse<T>>(text, Json);
        return (response.StatusCode, parsed);
    }

    public static async Task<T> OkAsync<T>(this HttpClient client, HttpMethod method, string url, object? body = null, string? token = null)
    {
        var (status, response) = await client.SendAsync<T>(method, url, body, token);
        Assert.True(status is HttpStatusCode.OK or HttpStatusCode.Created, $"{method} {url} => {(int)status}: {response?.Message} {string.Join("; ", response?.Errors.Select(e => e.Message) ?? [])}");
        return response!.Data!;
    }

    public static Task<AuthResponse> LoginAsync(this HttpClient client, string email, string password) =>
        client.OkAsync<AuthResponse>(HttpMethod.Post, "/api/auth/login", new LoginRequest { Email = email, Password = password });

    public static async Task<string> AdminTokenAsync(this HttpClient client) =>
        (await client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword)).AccessToken;

    /// <summary>Yönetici şoför ekler; şoför geçici şifreyle girip şifresini değiştirir.</summary>
    public static async Task<(Guid Id, string Token, string Email, string Password)> CreateDriverAsync(this HttpClient client, string adminToken, string? name = null)
    {
        var email = $"driver-{Guid.NewGuid():N}@test.local";
        var created = await client.OkAsync<UserCredentialResult>(HttpMethod.Post, "/api/auth/register",
            new CreateUserRequest { FullName = name ?? "Test Şoför", Email = email, PhoneNumber = "05551112233", Role = UserRole.User }, adminToken);
        Assert.False(created.SmsQueued);
        Assert.NotNull(created.TemporaryPassword);

        var login = await client.LoginAsync(email, created.TemporaryPassword!);
        const string password = "Sofor12345";
        var changed = await client.OkAsync<AuthResponse>(HttpMethod.Post, "/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = created.TemporaryPassword!, NewPassword = password }, login.AccessToken);
        return (created.User.Id, changed.AccessToken, email, password);
    }

    public static Task<CustomerDto> CreateCustomerAsync(this HttpClient client, string adminToken, string? email = "muhasebe@musteri.example") =>
        client.OkAsync<CustomerDto>(HttpMethod.Post, "/api/customers",
            new CustomerRequest { CompanyName = $"Müşteri {Guid.NewGuid():N}"[..20], Email = email, PhoneNumber = "02120000000" }, adminToken);

    public static Task<JobDetailDto> CreateJobAsync(this HttpClient client, string adminToken, Guid customerId, Guid? driverId = null, bool assignToMe = false,
        DateOnly? date = null, decimal price = 4000m) =>
        client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", new CreateJobRequest
        {
            CustomerId = customerId,
            AssignedDriverId = driverId,
            AssignToMe = assignToMe,
            JobDate = date ?? new DateOnly(2026, 10, 20),
            StoreName = "Starbucks",
            VehicleType = VehicleType.Panelvan,
            PickupAddress = "Gebze TLS",
            DeliveryAddress = "Haliç Tersane",
            Description = "Gebze TLS - Haliç Tersane",
            Price = price,
        }, adminToken);

    /// <summary>İşi kabulden tamamlanmaya kadar ilerletir.</summary>
    public static async Task<JobDetailDto> RunToCompletionAsync(this HttpClient client, Guid jobId, string token)
    {
        JobDetailDto job = null!;
        foreach (var action in new[] { "accept", "start", "start-loading", "start-delivery", "complete" })
        {
            var (status, body) = await client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{jobId}/{action}", new { }, token);
            if (action == "accept" && status == HttpStatusCode.Conflict) continue; // zaten kabul edilmiş (claim)
            Assert.Equal(HttpStatusCode.OK, status);
            job = body!.Data!;
        }
        return job;
    }
}
