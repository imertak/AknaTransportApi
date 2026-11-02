using System.Net;

namespace AknaTransportApi.Business.Tests.Integration;

public class VehicleTypeTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Default_types_are_listed_for_everyone()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var list = await _client.OkAsync<List<VehicleTypeDto>>(HttpMethod.Get, "/api/vehicle-types", token: driver.Token);
        Assert.Contains(list, t => t.Code == VehicleType.Panelvan && t.Name == "Panelvan");
        Assert.Contains(list, t => t.Code == VehicleType.SmallPanelvan && t.Name == "Küçük Panelvan");
    }

    [Fact]
    public async Task Admin_adds_type_and_uses_it_on_job_and_report()
    {
        var admin = await _client.AdminTokenAsync();
        var name = $"Tır {Guid.NewGuid():N}"[..12];
        var type = await _client.OkAsync<VehicleTypeDto>(HttpMethod.Post, "/api/vehicle-types", new VehicleTypeRequest { Name = name }, admin);
        Assert.StartsWith("Tir", type.Code);
        Assert.True(type.IsActive);

        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", new CreateJobRequest
        {
            CustomerId = customer.Id, JobDate = new DateOnly(2026, 10, 22), VehicleType = type.Code,
            PickupAddress = "A", DeliveryAddress = "B", Price = 100,
        }, admin);
        Assert.Equal(type.Code, job.Job.VehicleType);

        // Ad değişince kod sabit kalır; döküm özetinde yeni ad görünür.
        var renamed = await _client.OkAsync<VehicleTypeDto>(HttpMethod.Put, $"/api/vehicle-types/{type.Id}", new VehicleTypeRequest { Name = name + " X", IsActive = true }, admin);
        Assert.Equal(type.Code, renamed.Code);
        var summary = await _client.OkAsync<JobReportSummaryDto>(HttpMethod.Get, $"/api/reports/jobs/summary?scope=All&customerId={customer.Id}", token: admin);
        Assert.Contains(summary.ByVehicleType, g => g.Key == type.Code && g.Label == name + " X");
    }

    [Fact]
    public async Task Inactive_or_unknown_type_cannot_be_selected_but_existing_job_stays_editable()
    {
        var admin = await _client.AdminTokenAsync();
        var type = await _client.OkAsync<VehicleTypeDto>(HttpMethod.Post, "/api/vehicle-types", new VehicleTypeRequest { Name = $"Kasa {Guid.NewGuid():N}"[..12] }, admin);
        var customer = await _client.CreateCustomerAsync(admin);
        CreateJobRequest Request(string code) => new()
        {
            CustomerId = customer.Id, JobDate = new DateOnly(2026, 10, 23), VehicleType = code, PickupAddress = "A", DeliveryAddress = "B", Price = 100,
        };
        var job = await _client.OkAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", Request(type.Code), admin);

        await _client.OkAsync<VehicleTypeDto>(HttpMethod.Put, $"/api/vehicle-types/{type.Id}", new VehicleTypeRequest { Name = type.Name, IsActive = false }, admin);
        var (status, body) = await _client.SendAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", Request(type.Code), admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(body!.Errors!, e => e.Field == "vehicleType");

        (status, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Post, "/api/jobs", Request("YokBoyleTip"), admin);
        Assert.Equal(HttpStatusCode.BadRequest, status);

        // Pasif tipteki mevcut iş düzenlenebilir.
        var updated = await _client.OkAsync<JobDetailDto>(HttpMethod.Put, $"/api/jobs/{job.Job.Id}", new UpdateJobRequest
        {
            CustomerId = customer.Id, JobDate = new DateOnly(2026, 10, 24), VehicleType = type.Code, PickupAddress = "A", DeliveryAddress = "B", Price = 150,
        }, admin);
        Assert.Equal(150, updated.Job.Price);

        var active = await _client.OkAsync<List<VehicleTypeDto>>(HttpMethod.Get, "/api/vehicle-types", token: admin);
        Assert.DoesNotContain(active, t => t.Id == type.Id);
        var all = await _client.OkAsync<List<VehicleTypeDto>>(HttpMethod.Get, "/api/vehicle-types?includeInactive=true", token: admin);
        Assert.Contains(all, t => t.Id == type.Id && !t.IsActive);
    }

    [Fact]
    public async Task Duplicate_name_conflicts_and_driver_cannot_add()
    {
        var admin = await _client.AdminTokenAsync();
        var driver = await _client.CreateDriverAsync(admin);
        var (status, _) = await _client.SendAsync<VehicleTypeDto>(HttpMethod.Post, "/api/vehicle-types", new VehicleTypeRequest { Name = "panelvan" }, admin);
        Assert.Equal(HttpStatusCode.Conflict, status);
        (status, _) = await _client.SendAsync<VehicleTypeDto>(HttpMethod.Post, "/api/vehicle-types", new VehicleTypeRequest { Name = "Minibüs" }, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [InlineData("Kapalı Kasa Kamyon", "KapaliKasaKamyon")]
    [InlineData("Tır", "Tir")]
    [InlineData("10 Teker", "Tip10Teker")]
    [InlineData("Çekici / Dorse", "CekiciDorse")]
    public void Code_is_derived_from_name(string name, string code) => Assert.Equal(code, VehicleTypeService.ToCode(name));
}
