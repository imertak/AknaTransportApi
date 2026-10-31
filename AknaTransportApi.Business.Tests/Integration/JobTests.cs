using System.Net;

namespace AknaTransportApi.Business.Tests.Integration;

public class JobTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Driver_cannot_create_job()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);

        var (status, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", new CreateJobRequest
        {
            CustomerId = customer.Id, JobDate = new DateOnly(2026, 10, 20), StoreName = "X", VehicleType = VehicleType.Pickup,
            PickupAddress = "A", DeliveryAddress = "B", Description = "A - B", Price = 100,
        }, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task Admin_can_create_job_with_generated_unique_number()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var first = await _client.CreateJobAsync(admin, customer.Id);
        var second = await _client.CreateJobAsync(admin, customer.Id);

        Assert.Matches(@"^AKN-2026-\d{6}$", first.Job.JobNumber);
        Assert.NotEqual(first.Job.JobNumber, second.Job.JobNumber);
        Assert.Equal(JobStatus.Pending, first.Job.Status);
        Assert.Equal(customer.CompanyName, first.Job.CustomerName);
        Assert.Single(first.StatusHistory);
    }

    [Fact]
    public async Task Admin_can_create_job_with_new_customer()
    {
        var admin = await _client.AdminTokenAsync();
        var job = await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", new CreateJobRequest
        {
            NewCustomer = new() { CompanyName = "Yeni Firma A.Ş.", Email = "info@yenifirma.example" },
            JobDate = new DateOnly(2026, 10, 25), StoreName = "Yeni Firma", VehicleType = VehicleType.Truck,
            PickupAddress = "Tuzla", DeliveryAddress = "Esenyurt", Description = "Tuzla - Esenyurt", Price = 12500.75m,
        }, admin);
        Assert.Equal("Yeni Firma A.Ş.", job.Job.CustomerName);
        Assert.Equal(12500.75m, job.Job.Price);
    }

    [Fact]
    public async Task Admin_can_assign_job_to_self_and_complete_it_as_driver()
    {
        var adminAuth = await _client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword);
        var admin = adminAuth.AccessToken;
        var customer = await _client.CreateCustomerAsync(admin);

        // 1) Oluştururken kendine atama
        var own = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        Assert.Equal(adminAuth.User.Id, own.Job.AssignedDriverId);
        Assert.Equal(JobStatus.Assigned, own.Job.Status);

        // 2) Var olan işi üstlenme (claim)
        var pending = await _client.CreateJobAsync(admin, customer.Id);
        var claimed = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{pending.Job.Id}/claim", new { }, admin);
        Assert.Equal(JobStatus.Accepted, claimed.Job.Status);
        Assert.Equal(adminAuth.User.Id, claimed.Job.AssignedDriverId);

        // 3) assign ile kendine atama
        var other = await _client.CreateJobAsync(admin, customer.Id);
        var assigned = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{other.Job.Id}/assign", new AssignJobRequest { DriverId = adminAuth.User.Id }, admin);
        Assert.Equal(JobStatus.Assigned, assigned.Job.Status);

        var completed = await _client.RunToCompletionAsync(own.Job.Id, admin);
        Assert.Equal(JobStatus.Completed, completed.Job.Status);
        Assert.NotNull(completed.Job.CompletedAt);

        var history = await _client.OkAsync<List<JobStatusHistoryDto>>(HttpMethod.Get, $"/api/jobs/{own.Job.Id}/history", token: admin);
        Assert.Equal(6, history.Count);
        Assert.Equal(JobStatus.Completed, history[^1].ToStatus);
        Assert.Equal(JobStatus.Delivering, history[^1].FromStatus);
        Assert.Equal(adminAuth.User.Id, history[^1].ChangedByUserId);
    }

    [Fact]
    public async Task Driver_sees_only_own_jobs()
    {
        var admin = await _client.AdminTokenAsync();
        var d1 = await _client.CreateDriverAsync(admin);
        var d2 = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var mine = await _client.CreateJobAsync(admin, customer.Id, d1.Id);
        var theirs = await _client.CreateJobAsync(admin, customer.Id, d2.Id);

        var list = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, $"/api/jobs?pageSize=200&driverId={d2.Id}", token: d1.Token);
        Assert.All(list.Items, j => Assert.Equal(d1.Id, j.AssignedDriverId));
        Assert.Contains(list.Items, j => j.Id == mine.Job.Id);

        var myJobs = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs/my-jobs", token: d1.Token);
        Assert.Equal(1, myJobs.TotalCount);

        await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{mine.Job.Id}", token: d1.Token);
        var (status, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{theirs.Job.Id}", token: d1.Token);
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Fact]
    public async Task Driver_cannot_change_another_drivers_job_or_location()
    {
        var admin = await _client.AdminTokenAsync();
        var d1 = await _client.CreateDriverAsync(admin);
        var d2 = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var theirs = await _client.CreateJobAsync(admin, customer.Id, d2.Id);

        foreach (var action in new[] { "accept", "start", "complete" })
        {
            var (status, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{theirs.Job.Id}/{action}", new { }, d1.Token);
            Assert.Equal(HttpStatusCode.Forbidden, status);
        }

        var (cancel, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{theirs.Job.Id}/cancel", new { }, d1.Token);
        Assert.Equal(HttpStatusCode.Forbidden, cancel);

        // d2 işi yola çıkarır; d1 bu iş için konum gönderemez.
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{theirs.Job.Id}/accept", new { }, d2.Token);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{theirs.Job.Id}/start", new { }, d2.Token);
        var (loc, _) = await _client.SendAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update",
            new UpdateLocationRequest { JobId = theirs.Job.Id, Latitude = 41, Longitude = 29 }, d1.Token);
        Assert.Equal(HttpStatusCode.Forbidden, loc);

        var detail = await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{theirs.Job.Id}", token: admin);
        Assert.Equal(JobStatus.OnTheWay, detail.Job.Status);
    }

    [Fact]
    public async Task Completed_job_cannot_be_restarted()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driver.Id);
        await _client.RunToCompletionAsync(job.Job.Id, driver.Token);

        foreach (var action in new[] { "start", "start-loading", "start-delivery", "accept" })
        {
            var (status, body) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/{action}", new { }, driver.Token);
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.False(body!.IsSuccess);
        }
        var (adminCancel, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new { }, admin);
        Assert.Equal(HttpStatusCode.Conflict, adminCancel);
    }

    [Fact]
    public async Task Location_is_accepted_only_for_active_jobs_and_throttled()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driver.Id);

        var (notActive, _) = await _client.SendAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update",
            new UpdateLocationRequest { JobId = job.Job.Id, Latitude = 41, Longitude = 29 }, driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, notActive);

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/accept", new { }, driver.Token);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/start", new { }, driver.Token);

        var t0 = DateTime.UtcNow.AddMinutes(-10);
        var result = await _client.OkAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update", new UpdateLocationRequest
        {
            JobId = job.Job.Id,
            Points =
            [
                new() { Latitude = 41.0000, Longitude = 29.0000, RecordedAt = t0 },
                new() { Latitude = 41.00001, Longitude = 29.00001, RecordedAt = t0.AddSeconds(5) },  // çok yakın ve sık: atlanır
                new() { Latitude = 41.0100, Longitude = 29.0100, RecordedAt = t0.AddSeconds(60) },
                new() { Latitude = 41.0100, Longitude = 29.0100, RecordedAt = t0.AddSeconds(240) }, // heartbeat
            ],
        }, driver.Token);
        Assert.Equal(4, result.Received);
        Assert.Equal(3, result.Stored);
        Assert.Equal(41.01, result.Latest!.Latitude, 5);

        var latest = await _client.OkAsync<LocationDto>(HttpMethod.Get, $"/api/locations/jobs/{job.Job.Id}/latest", token: admin);
        Assert.Equal(t0.AddSeconds(240), latest.RecordedAt, TimeSpan.FromSeconds(1));

        var history = await _client.OkAsync<PagedResult<LocationDto>>(HttpMethod.Get, $"/api/locations/jobs/{job.Job.Id}/history?pageSize=2", token: admin);
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(2, history.Items.Count);

        var active = await _client.OkAsync<List<ActiveJobLocationDto>>(HttpMethod.Get, "/api/locations/active-jobs", token: driver.Token);
        Assert.Single(active);
        Assert.NotNull(active[0].Location);
    }

    [Fact]
    public async Task Invalid_job_request_returns_validation_errors()
    {
        var admin = await _client.AdminTokenAsync();
        var (status, body) = await _client.SendAsync<object>(HttpMethod.Post, "/api/jobs", new { price = -5 }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(body!.Errors, e => e.Field == "price");
        Assert.Contains(body.Errors, e => e.Field == "storeName");
    }
}
