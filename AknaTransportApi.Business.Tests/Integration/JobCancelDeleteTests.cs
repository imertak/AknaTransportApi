using System.Net;

namespace AknaTransportApi.Business.Tests.Integration;

/// <summary>İş iptali (her aşamada, neden zorunlu, tekrar güvenli) ve mantıksal silme (soft delete) senaryoları.</summary>
public class JobCancelDeleteTests(AknaApiFactory factory) : IClassFixture<AknaApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>Şoförlü işi verilen aşamaya kadar ilerletir.</summary>
    private async Task<(JobDetailDto Job, (Guid Id, string Token, string Email, string Password) Driver)> JobAtAsync(string admin, JobStatus target)
    {
        var driver = await _client.CreateDriverAsync(admin);
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id, driverId: target == JobStatus.Pending ? null : driver.Id);
        var steps = new (JobStatus Reached, string Action)[]
        {
            (JobStatus.Accepted, "accept"), (JobStatus.OnTheWay, "start"), (JobStatus.Loading, "start-loading"), (JobStatus.Delivering, "start-delivery"),
        };
        foreach (var (reached, action) in steps)
        {
            if (target is JobStatus.Pending or JobStatus.Assigned) break;
            job = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/{action}", new { }, driver.Token);
            if (reached == target) break;
        }
        Assert.Equal(target, job.Job.Status);
        return (job, driver);
    }

    [Theory]
    [InlineData(JobStatus.Pending)]
    [InlineData(JobStatus.Assigned)]
    [InlineData(JobStatus.Accepted)]
    [InlineData(JobStatus.OnTheWay)]
    [InlineData(JobStatus.Loading)]
    [InlineData(JobStatus.Delivering)]
    public async Task Job_can_be_cancelled_at_every_open_stage_with_audit(JobStatus stage)
    {
        var admin = await _client.AdminTokenAsync();
        var (job, _) = await JobAtAsync(admin, stage);

        var cancelled = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel",
            new CancelJobRequest { Reason = "  Müşteri vazgeçti " }, admin);

        Assert.Equal(JobStatus.Cancelled, cancelled.Job.Status);
        Assert.Equal("Müşteri vazgeçti", cancelled.Job.CancellationReason);
        Assert.NotNull(cancelled.Job.CancelledAt);
        Assert.NotNull(cancelled.Job.CancelledByUserId);
        Assert.False(string.IsNullOrWhiteSpace(cancelled.Job.CancelledByUserName));
        Assert.False(cancelled.Job.IsDeleted);
        var last = cancelled.StatusHistory[^1];
        Assert.Equal(stage, last.FromStatus);
        Assert.Equal(JobStatus.Cancelled, last.ToStatus);
        // Geçmiş kaybolmaz: önceki aşamaların kayıtları durur, şoför ve araç bilgisi korunur.
        Assert.Equal(job.StatusHistory.Count + 1, cancelled.StatusHistory.Count);
        Assert.Equal(job.Job.AssignedDriverId, cancelled.Job.AssignedDriverId);
    }

    [Fact]
    public async Task Repeated_cancel_is_idempotent_and_reason_is_required()
    {
        var admin = await _client.AdminTokenAsync();
        var (job, _) = await JobAtAsync(admin, JobStatus.OnTheWay);

        var (noReason, body) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = " " }, admin);
        Assert.Equal(HttpStatusCode.BadRequest, noReason);
        Assert.False(body!.IsSuccess);
        var (emptyBody, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", token: admin);
        Assert.Equal(HttpStatusCode.BadRequest, emptyBody);

        var first = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "Araç arızası" }, admin);
        var second = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "Başka neden" }, admin);

        Assert.Equal(first.Job.CancelledAt, second.Job.CancelledAt);
        Assert.Equal("Araç arızası", second.Job.CancellationReason);
        Assert.Single(second.StatusHistory, h => h.ToStatus == JobStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_stops_location_and_customer_tracking_and_notifies_driver()
    {
        var admin = await _client.AdminTokenAsync();
        var (job, driver) = await JobAtAsync(admin, JobStatus.OnTheWay);
        var link = await _client.OkAsync<TrackingTokenDto>(HttpMethod.Post, $"/api/jobs/{job.Job.Id}/tracking-links", new { }, admin);
        await _client.OkAsync<PublicTrackingDto>(HttpMethod.Get, $"/api/tracking/{link.Token}");

        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "Müşteri vazgeçti" }, admin);

        var (tracking, _) = await _client.SendAsync<PublicTrackingDto>(HttpMethod.Get, $"/api/tracking/{link.Token}");
        Assert.Equal(HttpStatusCode.NotFound, tracking);
        var (loc, _) = await _client.SendAsync<LocationUpdateResult>(HttpMethod.Post, "/api/locations/update",
            new UpdateLocationRequest { JobId = job.Job.Id, Latitude = 41, Longitude = 29 }, driver.Token);
        Assert.Equal(HttpStatusCode.Conflict, loc);

        var notifications = await _client.OkAsync<PagedResult<NotificationDto>>(HttpMethod.Get, "/api/notifications?pageSize=50", token: driver.Token);
        Assert.Contains(notifications.Items, n => n.Type == NotificationType.JobCancelled && n.JobId == job.Job.Id && n.Body.Contains("Müşteri vazgeçti"));
    }

    [Fact]
    public async Task Driver_cannot_cancel_delete_restore_or_see_deleted_jobs()
    {
        var admin = await _client.AdminTokenAsync();
        var (job, driver) = await JobAtAsync(admin, JobStatus.Accepted);

        var (cancel, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "x" }, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, cancel);
        var (delete, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "x" }, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, delete);
        var (deletedList, _) = await _client.SendAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs/deleted", token: driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, deletedList);

        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Hatalı kayıt" }, admin);
        var (restore, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/restore", new { }, driver.Token);
        Assert.Equal(HttpStatusCode.Forbidden, restore);
        // Şoför includeDeleted ile de silinmiş işi göremez.
        var (detail, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}?includeDeleted=true", token: driver.Token);
        Assert.Equal(HttpStatusCode.NotFound, detail);
        var mine = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs/my-jobs?pageSize=200", token: driver.Token);
        Assert.DoesNotContain(mine.Items, j => j.Id == job.Job.Id);
    }

    [Theory]
    [InlineData(JobStatus.Pending, false)]
    [InlineData(JobStatus.Assigned, false)]
    [InlineData(JobStatus.Accepted, true)]
    [InlineData(JobStatus.Loading, true)]
    [InlineData(JobStatus.Delivering, true)]
    public async Task Job_can_be_soft_deleted_at_every_stage(JobStatus stage, bool reasonRequired)
    {
        var admin = await _client.AdminTokenAsync();
        var (job, _) = await JobAtAsync(admin, stage);

        var (noReason, _) = await _client.SendAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", token: admin);
        if (reasonRequired)
        {
            Assert.Equal(HttpStatusCode.BadRequest, noReason);
            await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Mükerrer" }, admin);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, noReason);
        }

        // Standart liste ve detayda görünmez.
        var (get, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}", token: admin);
        Assert.Equal(HttpStatusCode.NotFound, get);
        var list = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs?pageSize=200", token: admin);
        Assert.DoesNotContain(list.Items, j => j.Id == job.Job.Id);

        // Yönetici silinmiş işi ve geçmişini görebilir; silme iptal ile karışmaz (durum korunur).
        var deleted = await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}?includeDeleted=true", token: admin);
        Assert.True(deleted.Job.IsDeleted);
        Assert.Equal(stage, deleted.Job.Status);
        Assert.NotNull(deleted.Job.DeletedAt);
        Assert.NotNull(deleted.Job.DeletedByUserId);
        Assert.Equal(reasonRequired ? "Mükerrer" : null, deleted.Job.DeletionReason);
        Assert.Equal(job.StatusHistory.Count, deleted.StatusHistory.Count);
        var trash = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, $"/api/jobs/deleted?search={job.Job.JobNumber}", token: admin);
        Assert.Contains(trash.Items, j => j.Id == job.Job.Id);

        // Tekrarlanan silme isteği hata vermez, veriyi değiştirmez.
        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", new DeleteJobRequest { Reason = "Başka" }, admin);
        var again = await _client.OkAsync<JobDetailDto>(HttpMethod.Get, $"/api/jobs/{job.Job.Id}?includeDeleted=true", token: admin);
        Assert.Equal(deleted.Job.DeletedAt, again.Job.DeletedAt);
        Assert.Equal(deleted.Job.DeletionReason, again.Job.DeletionReason);

        // Silinmiş iş üzerinde işlem yapılamaz.
        var (cancel, _) = await _client.SendAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "x" }, admin);
        Assert.Equal(HttpStatusCode.NotFound, cancel);
    }

    [Fact]
    public async Task Deleted_job_can_be_restored_and_cancelled_job_can_be_deleted()
    {
        var admin = await _client.AdminTokenAsync();
        var (job, _) = await JobAtAsync(admin, JobStatus.Assigned);
        await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/cancel", new CancelJobRequest { Reason = "Vazgeçildi" }, admin);
        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", token: admin);

        var restored = await _client.OkAsync<JobDetailDto>(HttpMethod.Patch, $"/api/jobs/{job.Job.Id}/restore", new { }, admin);
        Assert.False(restored.Job.IsDeleted);
        Assert.Null(restored.Job.DeletedAt);
        Assert.Equal(JobStatus.Cancelled, restored.Job.Status);
        Assert.Equal("Vazgeçildi", restored.Job.CancellationReason);
        var list = await _client.OkAsync<PagedResult<JobDto>>(HttpMethod.Get, "/api/jobs?pageSize=200", token: admin);
        Assert.Contains(list.Items, j => j.Id == job.Job.Id);
    }

    [Fact]
    public async Task Customer_with_only_deleted_jobs_is_deactivated_not_removed()
    {
        var admin = await _client.AdminTokenAsync();
        var customer = await _client.CreateCustomerAsync(admin);
        var job = await _client.CreateJobAsync(admin, customer.Id);
        await _client.OkAsync<object>(HttpMethod.Delete, $"/api/jobs/{job.Job.Id}", token: admin);

        var result = await _client.OkAsync<CustomerDeleteResult>(HttpMethod.Delete, $"/api/customers/{customer.Id}", token: admin);
        Assert.False(result.Deleted);
        Assert.True(result.Deactivated);
    }
}
