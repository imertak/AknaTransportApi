using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AknaTransportApi.Business.Services.Jobs;

public sealed class JobService(
    IRepository<TransportJob> jobs,
    IRepository<JobStatusHistory> history,
    IRepository<User> users,
    ICustomerService customerService,
    INumberGenerator numberGenerator,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<JobService> logger) : IJobService
{
    public async Task<PagedResult<JobDto>> ListAsync(JobQuery query, CancellationToken ct)
    {
        var q = jobs.QueryAsNoTracking();
        // Şoför yalnızca kendisine atanmış işleri görür; filtre parametresiyle bu kısıt aşılamaz.
        if (!currentUser.IsAdmin)
        {
            q = q.Where(j => j.AssignedDriverId == currentUser.UserId);
            query.DriverId = null;
        }
        return await Sort(ApplyFilters(q, query), query).Select(JobMappings.Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<PagedResult<JobDto>> ListMyJobsAsync(JobQuery query, CancellationToken ct)
    {
        var q = jobs.QueryAsNoTracking().Where(j => j.AssignedDriverId == currentUser.UserId);
        query.DriverId = null;
        return await Sort(ApplyFilters(q, query), query).Select(JobMappings.Projection).ToPagedResultAsync(query, ct);
    }

    public async Task<JobDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await jobs.QueryAsNoTracking().Where(j => j.Id == id).Select(JobMappings.Projection).FirstOrDefaultAsync(ct)
                  ?? throw NotFoundException.For("İş");
        EnsureCanView(dto.AssignedDriverId);
        var items = await LoadHistoryAsync(id, ct);
        return new JobDetailDto(dto, items);
    }

    public async Task<IReadOnlyList<JobStatusHistoryDto>> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        var driverId = await jobs.QueryAsNoTracking().Where(j => j.Id == id).Select(j => new { j.AssignedDriverId }).FirstOrDefaultAsync(ct)
                       ?? throw NotFoundException.For("İş");
        EnsureCanView(driverId.AssignedDriverId);
        return await LoadHistoryAsync(id, ct);
    }

    public async Task<JobDetailDto> CreateAsync(CreateJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var now = Now;
        var driverId = request.AssignToMe ? currentUser.UserId : request.AssignedDriverId;
        if (driverId is { } d && d != currentUser.UserId) await EnsureAssignableDriverAsync(d, ct);

        var jobId = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            Guid customerId;
            if (request.NewCustomer is not null)
            {
                customerId = customerService.AddWithoutSaving(request.NewCustomer).Id;
            }
            else
            {
                customerId = request.CustomerId!.Value;
                await customerService.EnsureExistsAsync(customerId, token);
            }

            var job = new TransportJob
            {
                CustomerId = customerId,
                CreatedByUserId = currentUser.UserId,
                Currency = request.Currency ?? Domain.Common.Money.DefaultCurrency,
            };
            job.Apply(request);
            job.JobNumber = await numberGenerator.NextJobNumberAsync(request.JobDate, token);
            job.Initialize(currentUser.UserId, driverId, now);
            jobs.Add(job);
            await unitOfWork.SaveChangesAsync(token);
            return job.Id;
        }, ct);

        logger.LogInformation("İş oluşturuldu. JobId: {JobId}", jobId);
        return await GetAsync(jobId, ct);
    }

    public async Task<JobDetailDto> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var job = await LoadForUpdateAsync(id, ct);
        job.EnsureEditable();
        if (job.CustomerId != request.CustomerId) await customerService.EnsureExistsAsync(request.CustomerId, ct);
        job.CustomerId = request.CustomerId;
        job.Apply(request);
        job.ConcurrencyStamp = Guid.NewGuid();
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var job = await LoadForUpdateAsync(id, ct);
        job.EnsureDeletable();
        jobs.Remove(job);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("İş silindi. JobId: {JobId}", id);
    }

    public async Task<JobDetailDto> AssignAsync(Guid id, AssignJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        if (request.DriverId is { } driverId && driverId != currentUser.UserId) await EnsureAssignableDriverAsync(driverId, ct);
        return await MutateAsync(id, job => job.AssignDriver(request.DriverId, currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: false, ct);
    }

    public Task<JobDetailDto> ClaimAsync(Guid id, JobActionRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        return MutateAsync(id, job => job.ClaimBy(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: false, ct);
    }

    public Task<JobDetailDto> AcceptAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.Accept(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.Start(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartLoadingAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.StartLoading(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartDeliveryAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.StartDelivery(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> CompleteAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.Complete(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> CancelAsync(Guid id, CancelJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        return MutateAsync(id, job => job.Cancel(currentUser.UserId, Now, request.Reason.TrimOrNull()), requireOwnership: false, ct);
    }

    public IQueryable<TransportJob> ApplyFilters(IQueryable<TransportJob> q, JobQuery query)
    {
        if (query.StartDate is { } start) q = q.Where(j => j.JobDate >= start);
        if (query.EndDate is { } end) q = q.Where(j => j.JobDate <= end);
        if (query.CustomerId is { } customerId) q = q.Where(j => j.CustomerId == customerId);
        if (query.DriverId is { } driverId) q = q.Where(j => j.AssignedDriverId == driverId);
        if (query.Status is { Count: > 0 } statuses) q = q.Where(j => statuses.Contains(j.Status));
        if (query.VehicleType is { } vehicle) q = q.Where(j => j.VehicleType == vehicle);
        if (query.StoreName.TrimOrNull() is { } store)
        {
            var s = store.ToLower();
            q = q.Where(j => j.StoreName.ToLower().Contains(s));
        }
        if (query.Search.TrimOrNull() is { } search)
        {
            var s = search.ToLower();
            q = q.Where(j => j.JobNumber.ToLower().Contains(s) || j.StoreName.ToLower().Contains(s) || j.Description.ToLower().Contains(s)
                             || j.PickupAddress.ToLower().Contains(s) || j.DeliveryAddress.ToLower().Contains(s));
        }
        if (query.Uninvoiced is { } uninvoiced) q = uninvoiced ? q.Where(j => j.InvoiceId == null) : q.Where(j => j.InvoiceId != null);
        return q;
    }

    private static IQueryable<TransportJob> Sort(IQueryable<TransportJob> q, JobQuery query) =>
        (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("price", true) => q.OrderByDescending(j => j.Price).ThenByDescending(j => j.Id),
            ("price", false) => q.OrderBy(j => j.Price).ThenBy(j => j.Id),
            ("createdat", true) => q.OrderByDescending(j => j.CreatedAt),
            ("createdat", false) => q.OrderBy(j => j.CreatedAt),
            ("jobnumber", true) => q.OrderByDescending(j => j.JobNumber),
            ("jobnumber", false) => q.OrderBy(j => j.JobNumber),
            ("status", true) => q.OrderByDescending(j => j.Status).ThenByDescending(j => j.JobDate),
            ("status", false) => q.OrderBy(j => j.Status).ThenByDescending(j => j.JobDate),
            ("storename", true) => q.OrderByDescending(j => j.StoreName),
            ("storename", false) => q.OrderBy(j => j.StoreName),
            (_, false) => q.OrderBy(j => j.JobDate).ThenBy(j => j.CreatedAt),
            _ => q.OrderByDescending(j => j.JobDate).ThenByDescending(j => j.CreatedAt),
        };

    private async Task<JobDetailDto> MutateAsync(Guid id, Action<TransportJob> action, bool requireOwnership, CancellationToken ct)
    {
        var job = await LoadForUpdateAsync(id, ct);
        if (requireOwnership) EnsureCanOperate(job);
        action(job);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("İş durumu güncellendi. JobId: {JobId}, Status: {Status}, ActorId: {ActorId}", job.Id, job.Status, currentUser.UserId);
        return await GetAsync(id, ct);
    }

    private async Task<TransportJob> LoadForUpdateAsync(Guid id, CancellationToken ct) =>
        await jobs.GetByIdAsync(id, ct) ?? throw NotFoundException.For("İş");

    private async Task<IReadOnlyList<JobStatusHistoryDto>> LoadHistoryAsync(Guid jobId, CancellationToken ct) =>
        await history.QueryAsNoTracking().Where(h => h.JobId == jobId).OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
            .Select(JobMappings.HistoryProjection).ToListAsync(ct);

    private async Task EnsureAssignableDriverAsync(Guid driverId, CancellationToken ct)
    {
        var ok = await users.QueryAsNoTracking().AnyAsync(u => u.Id == driverId && u.IsActive, ct);
        if (!ok) throw new BusinessValidationException("Seçilen şoför bulunamadı veya pasif durumda.", [new ErrorDetail("driverId", "Geçerli bir şoför seçin.")]);
    }

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
    }

    private void EnsureCanView(Guid? assignedDriverId)
    {
        if (!currentUser.IsAdmin && assignedDriverId != currentUser.UserId) throw new ForbiddenException("Bu işi görüntüleme yetkiniz yok.");
    }

    /// <summary>Şoför işlemleri: atanmış şoför veya yönetici yapabilir.</summary>
    private void EnsureCanOperate(TransportJob job)
    {
        if (!currentUser.IsAdmin && job.AssignedDriverId != currentUser.UserId) throw new ForbiddenException("Bu iş size atanmamış.");
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
