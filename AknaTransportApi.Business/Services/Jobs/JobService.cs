using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AknaTransportApi.Business.Services.Jobs;

public sealed class JobService(
    IRepository<TransportJob> jobs,
    IRepository<JobStatusHistory> history,
    IRepository<User> users,
    IRepository<DriverVehicle> driverVehicles,
    IRepository<InvoiceItem> invoiceItems,
    ICustomerService customerService,
    IVehicleTypeService vehicleTypes,
    INumberGenerator numberGenerator,
    IUnitOfWork unitOfWork,
    JobNotificationPublisher notifier,
    IEnumerable<IJobTrackingStopHandler> trackingStopHandlers,
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

    public Task<JobDetailDto> GetAsync(Guid id, CancellationToken ct) => GetAsync(id, includeDeleted: false, ct);

    public async Task<JobDetailDto> GetAsync(Guid id, bool includeDeleted, CancellationToken ct)
    {
        var q = jobs.QueryAsNoTracking();
        // Silinmiş işin detayı ve geçmişi yalnızca yöneticiye açıktır.
        if (includeDeleted && currentUser.IsAdmin) q = q.IgnoreQueryFilters();
        var dto = await q.Where(j => j.Id == id).Select(JobMappings.Projection).FirstOrDefaultAsync(ct)
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
        if (driverId is { } owner && request.AssignedVehicleId is { } vehicleId) await EnsureDriverVehicleAsync(owner, vehicleId, ct);
        await vehicleTypes.EnsureSelectableAsync(request.VehicleType, null, ct);

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
            job.SetStops(request.ResolveStops(), now);
            job.SetContacts(request.ResolveContacts(), now);
            job.JobNumber = await numberGenerator.NextJobNumberAsync(request.JobDate, token);
            job.Initialize(currentUser.UserId, driverId, now, driverId is null ? null : request.AssignedVehicleId);
            jobs.Add(job);
            await notifier.OnJobChangedAsync(job, null, null, currentUser.UserId, token);
            await unitOfWork.SaveChangesAsync(token);
            return job.Id;
        }, ct);

        logger.LogInformation("İş oluşturuldu. JobId: {JobId}", jobId);
        return await GetAsync(jobId, ct);
    }

    public async Task<JobDetailDto> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var job = await jobs.Query().Include(j => j.Stops).Include(j => j.Contacts).FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw NotFoundException.For("İş");
        job.EnsureEditable();
        await vehicleTypes.EnsureSelectableAsync(request.VehicleType, job.VehicleType, ct);
        if (job.CustomerId != request.CustomerId) await customerService.EnsureExistsAsync(request.CustomerId, ct);
        job.CustomerId = request.CustomerId;
        job.Apply(request);
        // Duraklar değiştiğinde güzergâh metni yeniden üretilir; elle yazılan açıklama korunur.
        job.SetStops(request.ResolveStops(), Now);
        // Eski istemciler kişi listesini göndermez; o durumda mevcut kişiler korunur.
        if (request.Contacts is not null) job.SetContacts(request.ResolveContacts(), Now);
        job.ConcurrencyStamp = Guid.NewGuid();
        notifier.OnJobUpdated(job, currentUser.UserId);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// İşi mantıksal olarak siler (soft delete). İş, durum geçmişi, duraklar, konum ve bildirim kayıtları korunur;
    /// iş yalnızca standart listelerden ve sorgulardan çıkar. Zaten silinmiş iş için istek başarıyla sonuçlanır (idempotent).
    /// </summary>
    public async Task DeleteAsync(Guid id, DeleteJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var job = await jobs.Query().IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw NotFoundException.For("İş");
        if (job.IsDeleted) return;
        job.EnsureDeletable();
        // Fatura kalemleri işe bağlıdır; iptal edilmiş faturada geçse bile fatura kaydı korunur.
        if (await invoiceItems.QueryAsNoTracking().AnyAsync(i => i.JobId == id, ct))
            throw new BusinessValidationException("Bu iş daha önce bir faturada kullanıldığı için silinemez. Fatura kayıtları korunur.");
        if (job.RequiresDeletionReason && request.Reason.TrimOrNull() is null)
            throw new BusinessValidationException("Devam eden veya tamamlanmış işi silmek için silme nedeni girilmelidir.",
                [new ErrorDetail("reason", "Silme nedenini yazın.")]);

        var wasTrackable = job.IsTrackable;
        job.SoftDelete(currentUser.UserId, Now, request.Reason.TrimOrNull());
        await notifier.OnJobDeletedAsync(job, currentUser.UserId, ct);
        if (!await TrySaveAsync(id, alreadyDone: j => j.IsDeleted, ct)) return;
        logger.LogInformation("İş silindi (mantıksal). JobId: {JobId}, ActorId: {ActorId}, Status: {Status}", id, currentUser.UserId, job.Status);
        await StopTrackingAsync(id, JobTrackingStopReason.Deleted, wasTrackable, ct);
    }

    public async Task<PagedResult<JobDto>> ListDeletedAsync(DeletedJobQuery query, CancellationToken ct)
    {
        EnsureAdmin();
        var q = jobs.QueryAsNoTracking().IgnoreQueryFilters().Where(j => j.IsDeleted);
        if (query.Search.TrimOrNull() is { } search)
        {
            var s = search.ToLower();
            q = q.Where(j => j.JobNumber.ToLower().Contains(s) || j.StoreName.ToLower().Contains(s)
                             || j.Customer!.CompanyName.ToLower().Contains(s) || j.RouteDescription.ToLower().Contains(s));
        }
        return await q.OrderByDescending(j => j.DeletedAt).ThenByDescending(j => j.Id).Select(JobMappings.Projection).ToPagedResultAsync(query, ct);
    }

    /// <summary>Silinmiş işi geri alır. İşin durumu ve geçmişi silinmeden önceki haliyle kalır.</summary>
    public async Task<JobDetailDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var job = await jobs.Query().IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == id, ct) ?? throw NotFoundException.For("İş");
        if (job.Restore())
        {
            await TrySaveAsync(id, alreadyDone: j => !j.IsDeleted, ct);
            logger.LogInformation("Silinen iş geri alındı. JobId: {JobId}, ActorId: {ActorId}", id, currentUser.UserId);
        }
        return await GetAsync(id, ct);
    }

    public async Task<JobDetailDto> AssignAsync(Guid id, AssignJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        if (request.DriverId is { } driverId && driverId != currentUser.UserId) await EnsureAssignableDriverAsync(driverId, ct);
        if (request.DriverId is { } owner && request.VehicleId is { } vehicleId) await EnsureDriverVehicleAsync(owner, vehicleId, ct);
        return await MutateAsync(id, job => job.AssignDriver(request.DriverId, currentUser.UserId, Now, request.Note.TrimOrNull(), request.VehicleId), requireOwnership: false, ct);
    }

    public async Task<JobDetailDto> ClaimAsync(Guid id, ClaimJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        // Yönetici kendi üzerine aldığı işte yalnızca kendisine atanmış aktif araçlardan birini seçebilir.
        if (request.VehicleId is { } vehicleId) await EnsureDriverVehicleAsync(currentUser.UserId, vehicleId, ct);
        return await MutateAsync(id, job => job.ClaimBy(currentUser.UserId, Now, request.Note.TrimOrNull(), request.VehicleId), requireOwnership: false, ct);
    }

    public Task<JobDetailDto> AcceptAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.Accept(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.Start(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartLoadingAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.StartLoading(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    public Task<JobDetailDto> StartDeliveryAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        MutateAsync(id, job => job.StartDelivery(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    /// <summary>
    /// Şoför yalnızca teslimattaki kendi işini tamamlar. Yönetici her işi (atanmamış olanlar dahil) tamamlanmamış
    /// her aşamadan doğrudan tamamlayabilir.
    /// </summary>
    public Task<JobDetailDto> CompleteAsync(Guid id, JobActionRequest request, CancellationToken ct) =>
        currentUser.IsAdmin
            ? MutateAsync(id, job => job.CompleteByAdmin(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: false, ct)
            : MutateAsync(id, job => job.Complete(currentUser.UserId, Now, request.Note.TrimOrNull()), requireOwnership: true, ct);

    /// <summary>
    /// İşi iptal eder. Tamamlanmamış her aşamada yapılabilir; neden zorunludur. İptal tarihi, iptal eden kullanıcı ve
    /// neden kaydedilir, durum geçmişine işlenir. Zaten iptal edilmiş iş için istek mevcut hali döndürür (idempotent).
    /// Şoföre ve diğer yöneticilere bildirim gider; canlı takip ve müşteri takip bağlantıları durur.
    /// </summary>
    public async Task<JobDetailDto> CancelAsync(Guid id, CancelJobRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var reason = request.Reason.TrimOrNull()
                     ?? throw new BusinessValidationException("İptal nedeni girilmelidir.", [new ErrorDetail("reason", "İptal nedenini yazın.")]);
        var job = await LoadForUpdateAsync(id, ct);
        var previousDriverId = job.AssignedDriverId;
        var previousStatus = job.Status;
        var wasTrackable = job.IsTrackable;
        if (!job.Cancel(currentUser.UserId, Now, reason)) return await GetAsync(id, ct);

        await notifier.OnJobChangedAsync(job, previousDriverId, previousStatus, currentUser.UserId, ct);
        if (await TrySaveAsync(id, alreadyDone: j => j.Status == JobStatus.Cancelled, ct))
        {
            logger.LogInformation("İş iptal edildi. JobId: {JobId}, FromStatus: {FromStatus}, ActorId: {ActorId}", id, previousStatus, currentUser.UserId);
            await StopTrackingAsync(id, JobTrackingStopReason.Cancelled, wasTrackable, ct);
        }
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Kaydeder. Aynı iş aynı anda başka bir istekle değiştirildiyse (ör. iptal butonuna iki kez basıldı) ve
    /// o istek aynı sonucu zaten üretmişse false döner; tekrarlanan istek hata vermez ve veri iki kez yazılmaz.
    /// </summary>
    private async Task<bool> TrySaveAsync(Guid id, Func<TransportJob, bool> alreadyDone, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            var current = await jobs.QueryAsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == id, ct);
            if (current is not null && alreadyDone(current)) return false;
            throw;
        }
    }

    /// <summary>İptal/silme sonrası canlı takibi durduran kancaları çalıştırır. Kanca hatası işlemi geri almaz.</summary>
    private async Task StopTrackingAsync(Guid jobId, JobTrackingStopReason reason, bool wasTrackable, CancellationToken ct)
    {
        foreach (var handler in trackingStopHandlers)
        {
            try
            {
                await handler.OnTrackingStoppedAsync(jobId, reason, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Canlı takip durdurma kancası başarısız. JobId: {JobId}, Handler: {Handler}, WasTrackable: {WasTrackable}",
                    jobId, handler.GetType().Name, wasTrackable);
            }
        }
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
                             || j.RouteDescription.ToLower().Contains(s) || j.Stops.Any(st => st.Address.ToLower().Contains(s)));
        }
        if (query.Uninvoiced is { } uninvoiced) q = uninvoiced ? q.Where(j => j.InvoiceId == null) : q.Where(j => j.InvoiceId != null);
        q = query.BillingStatus switch
        {
            JobBillingStatus.NotInvoiced => q.Where(j => j.InvoiceId == null),
            JobBillingStatus.InDraft => q.Where(j => j.InvoiceId != null && j.Invoice!.Status != InvoiceStatus.Issued),
            JobBillingStatus.Invoiced => q.Where(j => j.InvoiceId != null && j.Invoice!.Status == InvoiceStatus.Issued),
            _ => q,
        };
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
        var previousDriverId = job.AssignedDriverId;
        var previousStatus = job.Status;
        action(job);
        await notifier.OnJobChangedAsync(job, previousDriverId, previousStatus, currentUser.UserId, ct);
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

    /// <summary>Araç aktif olmalı ve şoföre aktif olarak atanmış olmalı.</summary>
    private async Task EnsureDriverVehicleAsync(Guid driverId, Guid vehicleId, CancellationToken ct)
    {
        var ok = await driverVehicles.QueryAsNoTracking()
            .AnyAsync(a => a.DriverId == driverId && a.VehicleId == vehicleId && a.IsActive && a.Vehicle!.IsActive, ct);
        if (!ok)
            throw new BusinessValidationException("Seçilen araç bu şoföre ait değil veya pasif durumda.",
                [new ErrorDetail("vehicleId", "Şoförün aktif araçlarından birini seçin.")]);
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
