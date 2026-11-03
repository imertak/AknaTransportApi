using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Integration;

/// <summary>SignalR canlı takip: kimlik doğrulama, yetkili gruplar ve konum / iş değişikliği yayını.</summary>
public class RealtimeTrackingTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private readonly HttpClient _client = factory.CreateClient();

    private HubConnection Connect(string? token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/tracking"), o =>
            {
                // Test sunucusu WebSocket desteklemez; aynı kimlik doğrulama akışı long polling ile sınanır.
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                if (token is not null) o.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

    private static Channel<T> Listen<T>(HubConnection connection, string method)
    {
        var channel = Channel.CreateUnbounded<T>();
        connection.On<T>(method, value => channel.Writer.TryWrite(value));
        return channel;
    }

    private static async Task<T> NextAsync<T>(Channel<T> channel, Func<T, bool> match)
    {
        using var cts = new CancellationTokenSource(Wait);
        while (true)
        {
            var item = await channel.Reader.ReadAsync(cts.Token);
            if (match(item)) return item;
        }
    }

    private static object Point(double lat, double lng, DateTime at) =>
        new { points = new[] { new { latitude = lat, longitude = lng, accuracy = 8.0, speed = 12.5, heading = 90.0, recordedAt = at } } };

    [Fact]
    public async Task Anonymous_connection_is_rejected()
    {
        await using var connection = Connect(null);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Admin_receives_live_location_and_job_changes_for_drivers_jobs()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var driver = await _client.CreateDriverAsync(admin);
        var job = (await _client.CreateJobAsync(admin, customer.Id, driver.Id)).Job;

        await using var adminHub = Connect(admin);
        var locations = Listen<LocationDto>(adminHub, "locationUpdated");
        var changes = Listen<JsonElement>(adminHub, "jobChanged");
        await using var driverHub = Connect(driver.Token);
        var driverChanges = Listen<JsonElement>(driverHub, "jobChanged");
        await adminHub.StartAsync();
        await driverHub.StartAsync();

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Id}/accept", new { }, driver.Token);
        var accepted = await NextAsync(changes, c => c.GetProperty("jobId").GetGuid() == job.Id);
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());
        // Şoför de kendi işinin değişikliğini alır (ör. yönetici iptal ederse takip anında durur).
        await NextAsync(driverChanges, c => c.GetProperty("jobId").GetGuid() == job.Id);

        var at = DateTime.UtcNow.AddSeconds(-5);
        await _client.OkAsync<DriverLocationUpdateResult>(HttpMethod.Post, "/api/locations/me", Point(41.0165, 29.1248, at), driver.Token);

        var live = await NextAsync(locations, l => l.JobId == job.Id);
        Assert.Equal(driver.Id, live.DriverId);
        Assert.Equal(41.0165, live.Latitude);
        Assert.Equal(90.0, live.Heading);
        Assert.Equal(12.5, live.Speed);

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Id}/cancel", new { reason = "Müşteri vazgeçti" }, admin);
        var cancelled = await NextAsync(driverChanges, c => c.GetProperty("jobId").GetGuid() == job.Id && c.GetProperty("status").GetString() == "Cancelled");
        Assert.Contains(driver.Id, cancelled.GetProperty("driverIds").EnumerateArray().Select(e => e.GetGuid()));
    }

    [Fact]
    public async Task Driver_cannot_watch_or_receive_other_drivers_locations()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var owner = await _client.CreateDriverAsync(admin, "Sahip Şoför");
        var other = await _client.CreateDriverAsync(admin, "Başka Şoför");
        var job = (await _client.CreateJobAsync(admin, customer.Id, owner.Id)).Job;
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Id}/accept", new { }, owner.Token);

        await using var otherHub = Connect(other.Token);
        var leaked = Listen<LocationDto>(otherHub, "locationUpdated");
        await otherHub.StartAsync();
        var ex = await Assert.ThrowsAsync<HubException>(() => otherHub.InvokeAsync("WatchJob", job.Id));
        Assert.Contains("yetkiniz yok", ex.Message);

        // Kendi işini izleyen şoför yetkili gruba katılabilir.
        await using var ownerHub = Connect(owner.Token);
        var own = Listen<LocationDto>(ownerHub, "locationUpdated");
        await ownerHub.StartAsync();
        await ownerHub.InvokeAsync("WatchJob", job.Id);

        await _client.OkAsync<DriverLocationUpdateResult>(HttpMethod.Post, "/api/locations/me", Point(40.99, 29.02, DateTime.UtcNow.AddSeconds(-3)), owner.Token);
        Assert.Equal(job.Id, (await NextAsync(own, l => l.JobId == job.Id)).JobId);

        // Diğer şoföre hiçbir konum gelmez.
        await Task.Delay(500);
        Assert.False(leaked.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Hub_rejects_account_that_must_change_password()
    {
        var admin = await _client.AdminTokenAsync();
        var email = $"temp-{Guid.NewGuid():N}@test.local";
        var created = await _client.OkAsync<UserCredentialResult>(HttpMethod.Post, "/api/auth/register",
            new CreateUserRequest { FullName = "Geçici", Email = email, PhoneNumber = "05551112233", Role = UserRole.User }, admin);
        var login = await _client.LoginAsync(email, created.TemporaryPassword!);

        await using var hub = Connect(login.AccessToken);
        var closed = new TaskCompletionSource();
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        try
        {
            await hub.StartAsync();
        }
        catch
        {
            return; // bağlantı başlatılamadı: beklenen
        }
        await closed.Task.WaitAsync(Wait);
        Assert.Equal(HubConnectionState.Disconnected, hub.State);
    }
}
