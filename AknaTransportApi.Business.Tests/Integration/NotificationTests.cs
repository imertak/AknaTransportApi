using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Business.Tests.Integration;

public class NotificationTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static string ExpoToken() => $"ExponentPushToken[{Guid.NewGuid():N}]";

    private async Task ProcessOutboxAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IOutboxProcessor>().ProcessPendingAsync(CancellationToken.None);
    }

    private Task<PagedResult<NotificationDto>> ListAsync(string token) =>
        _client.OkAsync<PagedResult<NotificationDto>>(HttpMethod.Get, "/api/notifications?pageSize=50", token: token);

    [Fact]
    public async Task Driver_gets_notification_and_push_when_job_is_assigned()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var token = ExpoToken();
        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/devices", new RegisterPushDeviceRequest(token, "android"), driver.Token);
        var customer = await _client.CreateCustomerAsync(admin);

        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);

        var list = await ListAsync(driver.Token);
        var n = Assert.Single(list.Items);
        Assert.Equal(NotificationType.JobAssigned, n.Type);
        Assert.Equal("Yeni iş atandı", n.Title);
        Assert.Contains(job.Job.JobNumber, n.Body);
        Assert.Equal(job.Job.Id, n.JobId);
        Assert.False(n.IsRead);
        Assert.Equal(1, (await _client.OkAsync<UnreadCountDto>(HttpMethod.Get, "/api/notifications/unread-count", token: driver.Token)).Count);

        await ProcessOutboxAsync();
        var push = Assert.Single(factory.Push.Sent, p => p.Token == token);
        Assert.Equal("Yeni iş atandı", push.Title);
        Assert.Equal(job.Job.Id.ToString(), push.Data["jobId"]);
        Assert.Equal(1, push.Badge);

        // Yönetici kendi yaptığı işlem için bildirim almaz.
        Assert.Empty((await ListAsync(admin)).Items.Where(x => x.JobId == job.Job.Id));
    }

    [Fact]
    public async Task Reassigning_notifies_new_and_previous_driver_and_cancel_notifies_driver()
    {
        var admin = await _client.AdminTokenAsync();
        var first = await _client.CreateDriverAsync(admin);
        var second = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: first.Id);

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/assign", new AssignJobRequest { DriverId = second.Id }, admin);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "Müşteri vazgeçti" }, admin);

        var firstTypes = (await ListAsync(first.Token)).Items.Select(i => i.Type).ToList();
        Assert.Equal([NotificationType.JobUnassigned, NotificationType.JobAssigned], firstTypes);
        var secondItems = (await ListAsync(second.Token)).Items;
        Assert.Equal([NotificationType.JobCancelled, NotificationType.JobAssigned], secondItems.Select(i => i.Type).ToList());
        Assert.Contains("Müşteri vazgeçti", secondItems[0].Body);
    }

    [Fact]
    public async Task Admin_is_notified_when_driver_progresses_job()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin, "Ahmet Yılmaz");
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);

        await _client.RunToCompletionAsync(job.Job.Id, driver.Token);

        var items = (await ListAsync(admin)).Items.Where(i => i.JobId == job.Job.Id).Select(i => i.Type).ToList();
        Assert.Equal([NotificationType.JobCompleted, NotificationType.JobDelivering, NotificationType.JobLoading, NotificationType.JobStarted, NotificationType.JobAccepted], items);
        var completed = (await ListAsync(admin)).Items.First(i => i.JobId == job.Job.Id);
        Assert.StartsWith("Ahmet Yılmaz,", completed.Body);
        // Şoför kendi işlemleri için bildirim almaz; yalnızca atama bildirimi vardır.
        Assert.Equal([NotificationType.JobAssigned], (await ListAsync(driver.Token)).Items.Select(i => i.Type).ToList());
    }

    [Fact]
    public async Task Mark_read_and_read_all_only_affect_own_notifications()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var other = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);
        await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);
        await _client.CreateJobAsync(admin, customer.Id, driverId: other.Id);

        var mine = (await ListAsync(driver.Token)).Items;
        var othersId = (await ListAsync(other.Token)).Items.Single().Id;
        var (status, _) = await _client.SendAsync<object>(HttpMethod.Post, $"/api/notifications/{othersId}/read", new { }, driver.Token);
        Assert.Equal(HttpStatusCode.NotFound, status);

        await _client.OkAsync<object>(HttpMethod.Post, $"/api/notifications/{mine[0].Id}/read", new { }, driver.Token);
        Assert.Equal(1, (await _client.OkAsync<UnreadCountDto>(HttpMethod.Get, "/api/notifications/unread-count", token: driver.Token)).Count);
        Assert.Single((await _client.OkAsync<PagedResult<NotificationDto>>(HttpMethod.Get, "/api/notifications?unreadOnly=true", token: driver.Token)).Items);

        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/read-all", new { }, driver.Token);
        Assert.Equal(0, (await _client.OkAsync<UnreadCountDto>(HttpMethod.Get, "/api/notifications/unread-count", token: driver.Token)).Count);
        Assert.Equal(1, (await _client.OkAsync<UnreadCountDto>(HttpMethod.Get, "/api/notifications/unread-count", token: other.Token)).Count);
    }

    [Fact]
    public async Task Device_token_moves_to_last_user_and_invalid_tokens_are_removed()
    {
        var admin = await _client.AdminTokenAsync();
        var first = await _client.CreateDriverAsync(admin);
        var second = await _client.CreateDriverAsync(admin);
        var token = ExpoToken();
        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/devices", new RegisterPushDeviceRequest(token, "ios"), first.Token);
        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/devices", new RegisterPushDeviceRequest(token, "ios"), second.Token);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var device = await db.PushDevices.SingleAsync(d => d.Token == token);
            Assert.Equal(second.Id, device.UserId);
        }

        factory.Push.InvalidTokens.Add(token);
        var customer = await _client.CreateCustomerAsync(admin);
        await _client.CreateJobAsync(admin, customer.Id, driverId: second.Id);
        await ProcessOutboxAsync();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.PushDevices.AnyAsync(d => d.Token == token));
        }
    }

    [Fact]
    public async Task Invalid_device_token_is_rejected_and_unregister_removes_device()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var (status, _) = await _client.SendAsync<object>(HttpMethod.Post, "/api/notifications/devices", new RegisterPushDeviceRequest("not-a-token", "android"), driver.Token);
        Assert.Equal(HttpStatusCode.BadRequest, status);

        var token = ExpoToken();
        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/devices", new RegisterPushDeviceRequest(token, "android"), driver.Token);
        await _client.OkAsync<object>(HttpMethod.Post, "/api/notifications/devices/remove", new UnregisterPushDeviceRequest(token), driver.Token);
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().PushDevices.AnyAsync(d => d.Token == token));
    }

    [Fact]
    public void Expo_ticket_errors_mark_device_not_registered_tokens_invalid()
    {
        var batch = new List<PushMessage>
        {
            new("ExponentPushToken[a]", "t", "b", new Dictionary<string, string>(), null),
            new("ExponentPushToken[b]", "t", "b", new Dictionary<string, string>(), null),
        };
        const string body = """{"data":[{"status":"ok","id":"1"},{"status":"error","message":"x","details":{"error":"DeviceNotRegistered"}}]}""";
        Assert.Equal(["ExponentPushToken[b]"], ExpoPushNotificationSender.ReadInvalidTokens(body, batch).ToList());
    }

    [Fact]
    public async Task Tracking_link_points_to_built_in_page_which_is_served()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);

        var link = await _client.OkAsync<TrackingTokenDto>(HttpMethod.Post, $"/api/jobs/{job.Job.Id}/tracking-links", new { }, admin);
        Assert.EndsWith($"/takip/?t={link.Token}", link.Url);

        var page = await _client.GetAsync("/takip/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("takip.js", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/takip/takip.js")).StatusCode);
        // Uygulamadaki haritanın stili (MapLibre) takip sayfasıyla birlikte sunulur.
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/takip/harita-stili.json")).StatusCode);

        var tracking = await _client.OkAsync<PublicTrackingDto>(HttpMethod.Get, $"/api/tracking/{link.Token}");
        Assert.Equal(job.Job.JobNumber, tracking.JobNumber);
        Assert.Null(tracking.VehiclePlate); // araç seçilmedi
    }

    [Theory]
    [InlineData("https://site.example/takip/{token}", "https://site.example/takip/abc")]
    [InlineData("https://site.example/takip/", "https://site.example/takip/?t=abc")]
    [InlineData("https://site.example/takip/?lang=tr", "https://site.example/takip/?lang=tr&t=abc")]
    public void Tracking_url_always_contains_token(string template, string expected) =>
        Assert.Equal(expected, LocationService.BuildTrackingUrl(template, "abc"));

    [Fact]
    public async Task Completed_job_can_be_deleted_and_disappears_from_lists()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: driver.Id);
        await _client.RunToCompletionAsync(job.Job.Id, driver.Token);

        // Tamamlanmış iş kritik sayılır: neden olmadan silinmez.
        var (noReason, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", token: admin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Mükerrer kayıt" }, admin);

        var (status, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}", token: admin);
        Assert.Equal(HttpStatusCode.NotFound, status);
        var list = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs?pageSize=200", token: admin);
        Assert.DoesNotContain(list.Items, j => j.Id == job.Job.Id);
        // Bildirimler kalır ama işe bağlı değildir.
        Assert.All((await ListAsync(admin)).Items, n => Assert.NotEqual(job.Job.Id, n.JobId));
    }

    [Fact]
    public async Task Invoiced_job_cannot_be_deleted()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(job.Job.Id, admin);
        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [job.Job.Id] }, admin);

        var (status, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Test" }, admin);
        Assert.NotEqual(HttpStatusCode.OK, status);

        // Fatura iptal edilse de kalem kaydı korunur; iş silinemez.
        await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/cancel", new { }, admin);
        (status, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Test" }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }
}
