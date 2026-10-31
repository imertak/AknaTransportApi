using System.Net;

namespace AknaTransportApi.Business.Tests.Integration;

/// <summary>Çoklu durak, müşteri adres geçmişi, araç ataması, konum takibi, faturalandırma durumu ve aylık gelir.</summary>
public class StopVehicleLocationTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static JobStopRequest Stop(StopType type, string address, double? lat = null, double? lng = null) =>
        new() { StopType = type, Address = address, Latitude = lat, Longitude = lng };

    private static CreateJobRequest MultiStopJob(Guid customerId, DateOnly? date = null, decimal price = 4000m) => new()
    {
        CustomerId = customerId,
        JobDate = date ?? new DateOnly(2026, 10, 19),
        StoreName = "Starbucks",
        VehicleType = VehicleType.Panelvan,
        Price = price,
        Description = "Gece yükleme",
        Stops =
        [
            Stop(StopType.Pickup, "İstanbul / Ümraniye", 41.0165, 29.1248),
            Stop(StopType.Delivery, "İstanbul / Zorlu AVM", 41.0671, 29.0170),
            Stop(StopType.Pickup, "İstanbul / İkitelli"),
            Stop(StopType.Pickup, "İstanbul / Gebze"),
            Stop(StopType.Delivery, "İstanbul / Haliç Tersane", 41.0383, 28.9469),
        ],
    };

    [Fact]
    public async Task Job_keeps_multiple_ordered_stops_and_builds_route_description()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);

        var job = (await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", MultiStopJob(customer.Id), admin)).Job;

        Assert.Equal("İstanbul / Ümraniye - İstanbul / İkitelli - İstanbul / Gebze - İstanbul / Zorlu AVM - İstanbul / Haliç Tersane", job.RouteDescription);
        Assert.Equal("Gece yükleme", job.Description);
        Assert.Equal(5, job.Stops.Count);
        Assert.Equal(["İstanbul / Ümraniye", "İstanbul / İkitelli", "İstanbul / Gebze"],
            job.Stops.Where(s => s.StopType == StopType.Pickup).OrderBy(s => s.Sequence).Select(s => s.Address));
        Assert.Equal([1, 2], job.Stops.Where(s => s.StopType == StopType.Delivery).Select(s => s.Sequence));
        // API sırası: önce tüm yükleme noktaları, sonra teslimat noktaları.
        Assert.Equal(
            [(StopType.Pickup, 1), (StopType.Pickup, 2), (StopType.Pickup, 3), (StopType.Delivery, 1), (StopType.Delivery, 2)],
            job.Stops.Select(s => (s.StopType, s.Sequence)));
        Assert.Equal("İstanbul / Ümraniye", job.PickupAddress);
        Assert.Equal("İstanbul / Haliç Tersane", job.DeliveryAddress);
        Assert.Equal(41.0383, job.DeliveryLatitude);

        // Duraklar güncellenince güzergâh yeniden üretilir, elle yazılan açıklama korunur.
        var updated = await _client.OkAsync<JobDetailDto>(HttpMethod.Put, $"/api/jobs/{job.Id}", new UpdateJobRequest
        {
            CustomerId = customer.Id, JobDate = job.JobDate, StoreName = job.StoreName, VehicleType = job.VehicleType, Price = job.Price,
            Description = job.Description,
            Stops = [Stop(StopType.Pickup, "İstanbul / Ümraniye"), Stop(StopType.Delivery, "İstanbul / Zorlu AVM")],
        }, admin);
        Assert.Equal("İstanbul / Ümraniye - İstanbul / Zorlu AVM", updated.Job.RouteDescription);
        Assert.Equal("Gece yükleme", updated.Job.Description);
        Assert.Equal(2, updated.Job.Stops.Count);
    }

    [Fact]
    public async Task Job_requires_at_least_one_pickup_and_one_delivery()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var request = MultiStopJob(customer.Id);
        request.Stops = [Stop(StopType.Pickup, "Ümraniye"), Stop(StopType.Pickup, "İkitelli")];

        var (status, body) = await _client.SendAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", request, admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(body!.Errors, e => e.Message.Contains("teslimat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Customer_address_history_is_distinct_and_scoped_to_customer()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var other = await _client.CreateCustomerAsync(admin);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", MultiStopJob(customer.Id), admin);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", MultiStopJob(customer.Id), admin);
        var foreign = MultiStopJob(other.Id);
        foreign.Stops = [Stop(StopType.Pickup, "Başka Müşteri Deposu"), Stop(StopType.Delivery, "Başka Mağaza")];
        await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", foreign, admin);

        var pickups = await _client.OkAsync<List<CustomerAddressDto>>(HttpMethod.Get, $"/api/customers/{customer.Id}/addresses?stopType=Pickup", token: admin);
        Assert.Equal(3, pickups.Count);
        Assert.All(pickups, a => Assert.Equal(StopType.Pickup, a.StopType));
        Assert.DoesNotContain(pickups, a => a.Address.Contains("Başka", StringComparison.Ordinal));
        var umraniye = Assert.Single(pickups, a => a.Address == "İstanbul / Ümraniye");
        Assert.Equal(2, umraniye.UseCount);
        Assert.Equal(41.0165, umraniye.Latitude);

        var all = await _client.OkAsync<List<CustomerAddressDto>>(HttpMethod.Get, $"/api/customers/{customer.Id}/addresses", token: admin);
        Assert.Equal(5, all.Count);
    }

    [Fact]
    public async Task Admin_assigns_job_with_drivers_own_active_vehicle_only()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var otherDriver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var van = await CreateVehicleAsync(admin);
        var foreignVan = await CreateVehicleAsync(admin);
        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{van.Id}/assign", new AssignVehicleRequest { DriverId = driver.Id }, admin);
        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{foreignVan.Id}/assign", new AssignVehicleRequest { DriverId = otherDriver.Id }, admin);

        // Seçilen şoförün aktif araçları
        var list = await _client.OkAsync<PagedResult<VehicleDto>>(HttpMethod.Get, $"/api/vehicles?driverId={driver.Id}&isActive=true", token: admin);
        Assert.Equal(van.Id, Assert.Single(list.Items).Id);

        var job = await _client.CreateJobAsync(admin, customer.Id);
        var (wrong, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/assign",
            new AssignJobRequest { DriverId = driver.Id, VehicleId = foreignVan.Id }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, wrong);

        var assigned = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/assign",
            new AssignJobRequest { DriverId = driver.Id, VehicleId = van.Id }, admin);
        Assert.Equal(van.Id, assigned.Job.AssignedVehicleId);
        Assert.Equal(van.PlateNumber, assigned.Job.AssignedVehiclePlate);

        // Şoför iş detayında aracı görür ve yalnızca kendi araçlarını listeler.
        var seen = await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}", token: driver.Token);
        Assert.Equal(van.PlateNumber, seen.Job.AssignedVehiclePlate);
        var mine = await _client.OkAsync<List<VehicleDto>>(HttpMethod.Get, "/api/vehicles/my", token: driver.Token);
        Assert.Equal(van.Id, Assert.Single(mine).Id);
        var (forbidden, _) = await _client.SendAsync<PagedResult<VehicleDto>>(HttpMethod.Get, "/api/vehicles", token: driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);

        // Pasif araç yeni işe atanamaz.
        await _client.OkAsync<VehicleDto>(HttpMethod.Put, $"/api/vehicles/{van.Id}", new VehicleRequest
        {
            PlateNumber = van.PlateNumber, VehicleType = van.VehicleType, IsActive = false,
        }, admin);
        var next = await _client.CreateJobAsync(admin, customer.Id);
        var (inactive, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{next.Job.Id}/assign",
            new AssignJobRequest { DriverId = driver.Id, VehicleId = van.Id }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, inactive);
    }

    [Fact]
    public async Task Vehicle_cannot_be_assigned_to_two_drivers_and_driver_can_have_many_vehicles()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var second = await _client.CreateDriverAsync(admin);
        var a = await CreateVehicleAsync(admin);
        var b = await CreateVehicleAsync(admin);

        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{a.Id}/assign", new AssignVehicleRequest { DriverId = driver.Id }, admin);
        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{b.Id}/assign", new AssignVehicleRequest { DriverId = driver.Id }, admin);
        var (conflict, _) = await _client.SendAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{a.Id}/assign", new AssignVehicleRequest { DriverId = second.Id }, admin);
        Assert.Equal(HttpStatusCode.Conflict, conflict);

        var mine = await _client.OkAsync<List<VehicleDto>>(HttpMethod.Get, "/api/vehicles/my", token: driver.Token);
        Assert.Equal(2, mine.Count);

        // Atama kaldırılınca araç başka şoföre verilebilir.
        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{a.Id}/unassign", token: admin);
        var moved = await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{a.Id}/assign", new AssignVehicleRequest { DriverId = second.Id }, admin);
        Assert.Equal(second.Id, moved.DriverId);

        var (duplicate, _) = await _client.SendAsync<VehicleDto>(HttpMethod.Post, "/api/vehicles", new VehicleRequest { PlateNumber = a.PlateNumber.ToLowerInvariant(), VehicleType = VehicleType.Truck }, admin);
        Assert.Equal(HttpStatusCode.Conflict, duplicate);
    }

    [Fact]
    public async Task Admin_claims_job_with_own_vehicle()
    {
        var adminAuth = await _client.LoginAsync(AknaApiFactory.AdminEmail, AknaApiFactory.AdminPassword);
        var admin = adminAuth.AccessToken;
        var customer = await _client.CreateCustomerAsync(admin);
        var own = await CreateVehicleAsync(admin);
        await _client.OkAsync<VehicleDto>(HttpMethod.Post, $"/api/vehicles/{own.Id}/assign", new AssignVehicleRequest { DriverId = adminAuth.User.Id }, admin);

        var job = await _client.CreateJobAsync(admin, customer.Id);
        var claimed = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/claim", new ClaimJobRequest { VehicleId = own.Id }, admin);
        Assert.Equal(JobStatus.Accepted, claimed.Job.Status);
        Assert.Equal(own.Id, claimed.Job.AssignedVehicleId);
    }

    [Fact]
    public async Task Location_tracking_starts_on_accept_and_stops_when_job_closes()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var other = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var first = await _client.CreateJobAsync(admin, customer.Id, driver.Id);
        var second = await _client.CreateJobAsync(admin, customer.Id, driver.Id);
        var point = new { points = new[] { new { latitude = 41.0, longitude = 29.0, accuracy = 10.0, recordedAt = DateTime.UtcNow } } };

        // Kabul edilmeden konum kabul edilmez.
        var (before, _) = await _client.SendAsync<DriverLocationUpdateResult>(HttpMethod.Post, "/api/locations/me", point, driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, before);

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{first.Job.Id}/accept", new { }, driver.Token);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{second.Job.Id}/accept", new { }, driver.Token);

        // Tek konum akışı şoförün iki aktif işine birden yazılır.
        var result = await _client.OkAsync<DriverLocationUpdateResult>(HttpMethod.Post, "/api/locations/me", point, driver.Token);
        Assert.Equal(2, result.JobIds.Count);
        Assert.All(result.Results, r => Assert.Equal(1, r.Stored));

        // Başka bir şoför bu işe konum gönderemez.
        var (foreign, _) = await _client.SendAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update",
            new UpdateLocationRequest { JobId = first.Job.Id, Latitude = 41, Longitude = 29 }, other.Token);
        Assert.Equal(HttpStatusCode.Forbidden, foreign);

        // Tamamlanan ve iptal edilen işlere yeni konum eklenmez.
        await _client.RunToCompletionAsync(first.Job.Id, driver.Token);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{second.Job.Id}/cancel", new CancelJobRequest { Reason = "Test" }, admin);
        var (closed, _) = await _client.SendAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update",
            new UpdateLocationRequest { JobId = first.Job.Id, Latitude = 41.1, Longitude = 29.1 }, driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, closed);
        var (after, _) = await _client.SendAsync<DriverLocationUpdateResult>(HttpMethod.Post, "/api/locations/me", point, driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, after);
    }

    [Fact]
    public async Task Billing_status_follows_invoice_state()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var a = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        var b = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true);
        await _client.RunToCompletionAsync(a.Job.Id, admin);
        await _client.RunToCompletionAsync(b.Job.Id, admin);
        Assert.Equal(JobBillingStatus.NotInvoiced, (await GetJob(a.Job.Id, admin)).BillingStatus);

        var invoice = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, "/api/invoices", new CreateInvoiceRequest { JobIds = [a.Job.Id, b.Job.Id] }, admin);
        Assert.Equal(2, invoice.Items.Count);
        Assert.Equal(8000m, invoice.SubTotal);
        Assert.Equal(JobBillingStatus.InDraft, (await GetJob(a.Job.Id, admin)).BillingStatus);

        var drafts = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, $"/api/jobs?customerId={customer.Id}&billingStatus=InDraft", token: admin);
        Assert.Equal(2, drafts.TotalCount);

        var issued = await _client.OkAsync<InvoiceDetailDto>(HttpMethod.Post, $"/api/invoices/{invoice.Id}/issue", token: admin);
        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.Equal(JobBillingStatus.Invoiced, (await GetJob(b.Job.Id, admin)).BillingStatus);

        // Döküm PDF'i yalnızca bu faturanın işlerini içerir.
        var pdf = await _client.GetAsync($"/api/invoices/{invoice.Id}/job-report/pdf");
        Assert.Equal(HttpStatusCode.Unauthorized, pdf.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/invoices/{invoice.Id}/job-report/pdf");
        request.Headers.Authorization = new("Bearer", admin);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Monthly_revenue_counts_only_completed_jobs_of_that_month()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var month = new DateOnly(2031, 3, 1);
        var done1 = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true, date: month.AddDays(2), price: 4000m);
        var done2 = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true, date: month.AddDays(20), price: 8000m);
        var cancelled = await _client.CreateJobAsync(admin, customer.Id, date: month.AddDays(5), price: 10000m);
        await _client.CreateJobAsync(admin, customer.Id, date: month.AddDays(6), price: 2500m); // bekleyen iş
        var nextMonth = await _client.CreateJobAsync(admin, customer.Id, assignToMe: true, date: month.AddMonths(1), price: 3000m);
        await _client.RunToCompletionAsync(done1.Job.Id, admin);
        await _client.RunToCompletionAsync(done2.Job.Id, admin);
        await _client.RunToCompletionAsync(nextMonth.Job.Id, admin);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{cancelled.Job.Id}/cancel", new CancelJobRequest(), admin);

        var revenue = await _client.OkAsync<MonthRevenueSummaryDto>(HttpMethod.Get, "/api/dashboard/revenue?year=2031&month=3", token: admin);
        Assert.Equal(12000m, revenue.Revenue);
        Assert.Equal(2, revenue.CompletedJobs);
        Assert.Equal(new DateOnly(2031, 3, 31), revenue.EndDate);

        var (bad, _) = await _client.SendAsync<MonthRevenueSummaryDto>(HttpMethod.Get, "/api/dashboard/revenue?year=2031&month=13", token: admin);
        Assert.Equal(HttpStatusCode.BadRequest, bad);
        var driver = await _client.CreateDriverAsync(admin);
        var (forbidden, _) = await _client.SendAsync<MonthRevenueSummaryDto>(HttpMethod.Get, "/api/dashboard/revenue", token: driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
    }

    private async Task<JobDto> GetJob(Guid id, string token) => (await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{id}", token: token)).Job;

    private Task<VehicleDto> CreateVehicleAsync(string admin) =>
        _client.OkAsync<VehicleDto>(HttpMethod.Post, "/api/vehicles", new VehicleRequest
        {
            PlateNumber = $"34 T {Random.Shared.Next(10000, 99999)}", Brand = "Ford", Model = "Transit", VehicleType = VehicleType.Panelvan,
        }, admin);
}
