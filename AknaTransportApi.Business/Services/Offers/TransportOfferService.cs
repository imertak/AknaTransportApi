using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Services.Offers;

/// <summary>
/// Nakliye teklifleri. Tüm işlemler yöneticiye açıktır (controller politikası + servis kontrolü). Sistem tek firmalıdır:
/// teklifler AKNA'ya aittir, şoför hesapları teklif göremez. Tutarlar her kayıtta kalemlerden yeniden hesaplanır;
/// istemcinin gönderdiği toplamlar kabul edilmez. Oluşturan kullanıcı JWT'den alınır.
/// </summary>
public sealed class TransportOfferService(
    IRepository<TransportOffer> offers,
    IRepository<TransportJob> jobs,
    ICustomerService customerService,
    IVehicleTypeService vehicleTypes,
    IJobService jobService,
    INumberGenerator numberGenerator,
    IUnitOfWork unitOfWork,
    ITransportOfferPdfGenerator pdfGenerator,
    IBusinessCalendar calendar,
    IOptions<AppOptions> appOptions,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<TransportOfferService> logger) : ITransportOfferService
{
    private static readonly TransportOfferStatus[] Expirable = [TransportOfferStatus.Draft, TransportOfferStatus.Pending];

    public async Task<PagedResult<TransportOfferListItemDto>> ListAsync(TransportOfferQuery query, CancellationToken ct)
    {
        EnsureAdmin();
        var today = calendar.Today;
        var q = ApplyFilters(offers.QueryAsNoTracking(), query, today, includeStatus: true);
        var page = await Sort(q, query)
            .Select(o => new TransportOfferListItemDto(
                o.Id, o.OfferNumber, o.OfferDate, o.ValidUntil, o.CustomerId, o.CustomerName, o.LoadingPoint, o.UnloadingPoint,
                o.TransportType, o.VehicleType, o.TotalAmount, o.Currency, o.Status, o.ConvertedJobId, o.CreatedAt))
            .ToPagedResultAsync(query, ct);
        // Süresi dolma veritabanında saklanmaz; okunurken türetilir.
        return new PagedResult<TransportOfferListItemDto>
        {
            Items = page.Items.Select(i => i with { Status = TransportOffer.ResolveStatus(i.Status, i.ValidUntil, today) }).ToList(),
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
        };
    }

    public async Task<TransportOfferStatsDto> StatsAsync(TransportOfferQuery query, CancellationToken ct)
    {
        EnsureAdmin();
        var today = calendar.Today;
        var rows = await ApplyFilters(offers.QueryAsNoTracking(), query, today, includeStatus: false)
            .GroupBy(o => new { o.Status, Past = o.ValidUntil < today })
            .Select(g => new { g.Key.Status, g.Key.Past, Count = g.Count(), Total = g.Sum(o => (decimal?)o.TotalAmount) ?? 0m })
            .ToListAsync(ct);

        var byStatus = rows
            .Select(r => (Status: Expirable.Contains(r.Status) && r.Past ? TransportOfferStatus.Expired : r.Status, r.Count, r.Total))
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => (Count: g.Sum(x => x.Count), Total: g.Sum(x => x.Total)));
        int Count(TransportOfferStatus s) => byStatus.TryGetValue(s, out var v) ? v.Count : 0;
        decimal Total(TransportOfferStatus s) => byStatus.TryGetValue(s, out var v) ? v.Total : 0m;

        return new TransportOfferStatsDto(
            rows.Sum(r => r.Count),
            Count(TransportOfferStatus.Draft),
            Count(TransportOfferStatus.Pending),
            Count(TransportOfferStatus.Accepted),
            Count(TransportOfferStatus.Rejected),
            Count(TransportOfferStatus.Expired),
            Count(TransportOfferStatus.Cancelled),
            Total(TransportOfferStatus.Accepted),
            Total(TransportOfferStatus.Pending));
    }

    public async Task<TransportOfferDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var offer = await LoadDetailAsync(id, ct);
        return await ToDetailAsync(offer, ct);
    }

    public async Task<TransportOfferDetailDto> CreateAsync(TransportOfferRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        await vehicleTypes.EnsureSelectableAsync(request.VehicleType, null, ct);
        var now = Now;
        var (offerDate, validUntil) = ResolveDates(request, calendar.Today);

        var offerId = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var customerId = await ResolveCustomerAsync(request, token);
            var offer = new TransportOffer
            {
                CustomerId = customerId,
                CreatedByUserId = currentUser.UserId,
                OfferDate = offerDate,
                ValidUntil = validUntil,
                Currency = request.Currency ?? Money.DefaultCurrency,
            };
            Apply(offer, request);
            offer.SetPricing(ResolveItems(request), request.DiscountAmount, ResolveVatRate(request), now);
            offer.OfferNumber = await numberGenerator.NextOfferNumberAsync(offerDate, token);
            offer.Initialize(currentUser.UserId, now);
            offers.Add(offer);
            await unitOfWork.SaveChangesAsync(token);
            return offer.Id;
        }, ct);

        logger.LogInformation("Teklif oluşturuldu. OfferId: {OfferId}, ActorId: {ActorId}", offerId, currentUser.UserId);
        return await GetAsync(offerId, ct);
    }

    public async Task<TransportOfferDetailDto> UpdateAsync(Guid id, TransportOfferRequest request, CancellationToken ct)
    {
        EnsureAdmin();
        var offer = await offers.Query().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFoundException.For("Teklif");
        offer.EnsureEditable();
        await vehicleTypes.EnsureSelectableAsync(request.VehicleType, offer.VehicleType, ct);
        var now = Now;
        var (offerDate, validUntil) = ResolveDates(request, calendar.Today, offer.OfferDate);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (request.CustomerId is { } customerId)
            {
                if (customerId != offer.CustomerId) await customerService.EnsureExistsAsync(customerId, token);
                offer.CustomerId = customerId;
            }
            else
            {
                offer.CustomerId = await ResolveCustomerAsync(request, token);
            }
            offer.OfferDate = offerDate;
            offer.ValidUntil = validUntil;
            Apply(offer, request);
            offer.SetPricing(ResolveItems(request), request.DiscountAmount, ResolveVatRate(request), now);
            offer.History.Add(new TransportOfferHistory
            {
                TransportOfferId = offer.Id,
                OldStatus = offer.Status,
                NewStatus = offer.Status,
                ChangedByUserId = currentUser.UserId,
                CreatedAt = now,
                Description = "Teklif güncellendi.",
            });
            await unitOfWork.SaveChangesAsync(token);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<TransportOfferDetailDto> CopyAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var source = await offers.QueryAsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFoundException.For("Teklif");
        var today = calendar.Today;
        var now = Now;

        var copyId = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var copy = new TransportOffer
            {
                CustomerId = source.CustomerId,
                CustomerName = source.CustomerName,
                ContactPerson = source.ContactPerson,
                CustomerPhone = source.CustomerPhone,
                CustomerEmail = source.CustomerEmail,
                CustomerAddress = source.CustomerAddress,
                OfferDate = today,
                ValidUntil = today.AddDays(TransportOffer.DefaultValidityDays),
                LoadingPoint = source.LoadingPoint,
                LoadingAddress = source.LoadingAddress,
                UnloadingPoint = source.UnloadingPoint,
                UnloadingAddress = source.UnloadingAddress,
                TransportType = source.TransportType,
                VehicleType = source.VehicleType,
                CargoDescription = source.CargoDescription,
                // Geçmiş tarihler kopyaya taşınmaz.
                LoadingDate = source.LoadingDate >= today ? source.LoadingDate : null,
                DeliveryDate = source.DeliveryDate >= today ? source.DeliveryDate : null,
                DistanceKm = source.DistanceKm,
                Currency = source.Currency,
                PaymentTerms = source.PaymentTerms,
                DeliveryTerms = source.DeliveryTerms,
                CustomerNotes = source.CustomerNotes,
                InternalNotes = source.InternalNotes,
                CopiedFromOfferId = source.Id,
                CreatedByUserId = currentUser.UserId,
            };
            copy.SetPricing(
                source.Items.OrderBy(i => i.Sequence).Select(i => new TransportOfferItemInput(i.Description, i.Quantity, i.UnitPrice, i.Unit)).ToList(),
                source.DiscountAmount, source.VatRate, now);
            copy.OfferNumber = await numberGenerator.NextOfferNumberAsync(today, token);
            copy.Initialize(currentUser.UserId, now, $"Teklif {source.OfferNumber} kopyalanarak oluşturuldu.");
            offers.Add(copy);
            await unitOfWork.SaveChangesAsync(token);
            return copy.Id;
        }, ct);

        logger.LogInformation("Teklif kopyalandı. SourceId: {SourceId}, OfferId: {OfferId}", id, copyId);
        return await GetAsync(copyId, ct);
    }

    public Task<TransportOfferDetailDto> MarkSentAsync(Guid id, CancellationToken ct) =>
        MutateAsync(id, o => o.MarkSent(currentUser.UserId, Now, calendar.Today), ct);

    public Task<TransportOfferDetailDto> AcceptAsync(Guid id, OfferStatusRequest request, CancellationToken ct) =>
        MutateAsync(id, o => o.Accept(currentUser.UserId, Now, calendar.Today, request.Reason.TrimOrNull()), ct);

    public Task<TransportOfferDetailDto> RejectAsync(Guid id, OfferStatusRequest request, CancellationToken ct) =>
        MutateAsync(id, o => o.Reject(currentUser.UserId, Now, request.Reason.TrimOrNull()), ct);

    public Task<TransportOfferDetailDto> CancelAsync(Guid id, OfferStatusRequest request, CancellationToken ct)
    {
        var reason = request.Reason.TrimOrNull()
                     ?? throw new BusinessValidationException("İptal nedeni girilmelidir.", [new ErrorDetail("reason", "İptal nedenini yazın.")]);
        return MutateAsync(id, o => o.Cancel(currentUser.UserId, Now, reason), ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var offer = await offers.Query().IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFoundException.For("Teklif");
        if (!offer.SoftDelete(currentUser.UserId, Now)) return;
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Teklif silindi (mantıksal). OfferId: {OfferId}, ActorId: {ActorId}", id, currentUser.UserId);
    }

    public async Task<FileDownload> GetPdfAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var offer = await LoadDetailAsync(id, ct);
        var detail = await ToDetailAsync(offer, ct);
        var options = appOptions.Value;
        var status = detail.Status;
        var document = new TransportOfferDocument
        {
            OfferNumber = detail.OfferNumber,
            OfferDate = detail.OfferDate,
            ValidUntil = detail.ValidUntil,
            StatusLabel = status switch
            {
                TransportOfferStatus.Cancelled => "İPTAL EDİLDİ",
                TransportOfferStatus.Rejected => "REDDEDİLDİ",
                TransportOfferStatus.Expired => "SÜRESİ DOLDU",
                TransportOfferStatus.Accepted => "KABUL EDİLDİ",
                _ => string.Empty,
            },
            // Taslak ve yanıt bekleyen teklif müşteriye gönderilen belgedir; üzerinde durum yazmaz.
            ShowStatus = status is TransportOfferStatus.Cancelled or TransportOfferStatus.Rejected or TransportOfferStatus.Expired,
            CustomerName = detail.CustomerName,
            ContactPerson = detail.ContactPerson,
            CustomerPhone = detail.CustomerPhone,
            CustomerEmail = detail.CustomerEmail,
            CustomerAddress = detail.CustomerAddress,
            LoadingPoint = detail.LoadingPoint,
            LoadingAddress = detail.LoadingAddress,
            UnloadingPoint = detail.UnloadingPoint,
            UnloadingAddress = detail.UnloadingAddress,
            TransportTypeLabel = TransportTypeLabel(detail.TransportType),
            VehicleTypeName = detail.VehicleTypeName,
            CargoDescription = detail.CargoDescription,
            LoadingDate = detail.LoadingDate,
            DeliveryDate = detail.DeliveryDate,
            DistanceKm = detail.DistanceKm,
            Items = detail.Items,
            SubTotal = detail.SubTotal,
            DiscountAmount = detail.DiscountAmount,
            VatRate = detail.VatRate,
            VatAmount = detail.VatAmount,
            TotalAmount = detail.TotalAmount,
            Currency = detail.Currency,
            AmountInWords = AmountInWords.Write(detail.TotalAmount, detail.Currency),
            PaymentTerms = detail.PaymentTerms,
            DeliveryTerms = detail.DeliveryTerms,
            CustomerNotes = detail.CustomerNotes,
            CompanyName = options.CompanyName,
            CompanyAddress = options.CompanyAddress.TrimOrNull(),
            CompanyPhone = options.CompanyPhone.TrimOrNull(),
            CompanyEmail = options.CompanyEmail.TrimOrNull(),
            CompanyWebsite = options.CompanyWebsite.TrimOrNull(),
            CompanyTaxOffice = options.CompanyTaxOffice.TrimOrNull(),
            CompanyTaxNumber = options.CompanyTaxNumber.TrimOrNull(),
            CompanyIban = options.CompanyIban.TrimOrNull() is { } iban ? Iban.Format(iban) : null,
            CompanyBankName = options.CompanyBankName.TrimOrNull(),
            PreparedBy = detail.CreatedByUserName,
            GeneratedAtUtc = Now,
        };
        return new FileDownload(pdfGenerator.Generate(document), detail.PdfFileName);
    }

    public async Task<ConvertOfferResultDto> ConvertToJobAsync(Guid id, CancellationToken ct)
    {
        EnsureAdmin();
        var today = calendar.Today;
        (Guid JobId, string JobNumber) created;
        try
        {
            created = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                // Önce teklif satırı koşullu güncellemeyle kilitlenir: eşzamanlı ikinci istek burada bekler, ilk istek
                // tamamlanınca koşul (ConvertedJobId IS NULL) artık sağlanmadığı için hiçbir satırı güncellemez.
                // Böylece iki işlem birbirini kilitlemez (deadlock) ve aynı tekliften ikinci iş oluşmaz.
                var claimStamp = Guid.NewGuid();
                var claimed = await offers.Query()
                    .Where(o => o.Id == id && o.Status == TransportOfferStatus.Accepted && o.ConvertedJobId == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(o => o.ConcurrencyStamp, claimStamp), token);
                var offer = await offers.Query().FirstOrDefaultAsync(o => o.Id == id, token) ?? throw NotFoundException.For("Teklif");
                offer.EnsureConvertible();
                if (claimed == 0) throw new ConflictException("Teklif aynı anda başka bir işlemle değiştirildi. Lütfen yenileyip tekrar deneyin.");

                // Mevcut iş oluşturma servisi kullanılır: araç tipi, müşteri ve durak kuralları aynen uygulanır.
                var job = await jobService.CreateAsync(BuildJobRequest(offer, today), token);
                offer.MarkConverted(job.Job.Id, job.Job.JobNumber, currentUser.UserId, Now);
                // Aynı teklif eşzamanlı iki istekle dönüştürülürse ikinci kayıt concurrency stamp nedeniyle reddedilir
                // ve transaction geri alınır (oluşturulan iş de silinir).
                await unitOfWork.SaveChangesAsync(token);
                return (job.Job.Id, job.Job.JobNumber);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Bu teklif aynı anda başka bir işlemle değiştirildi veya zaten nakliye işine dönüştürüldü. Lütfen yenileyin.");
        }

        logger.LogInformation("Teklif nakliye işine dönüştürüldü. OfferId: {OfferId}, JobId: {JobId}", id, created.JobId);
        return new ConvertOfferResultDto(await GetAsync(id, ct), created.JobId, created.JobNumber);
    }

    /// <summary>Teklif bilgilerinden iş oluşturma isteği. İş fiyatı indirim sonrası KDV hariç tutardır (KDV faturada eklenir).</summary>
    internal static CreateJobRequest BuildJobRequest(TransportOffer offer, DateOnly today)
    {
        var typeLabel = TransportTypeLabel(offer.TransportType);
        var description = string.Join(" · ", new[] { typeLabel, offer.CargoDescription.TrimOrNull() }.Where(s => s is not null));
        var notes = string.Join("\n", new[]
        {
            $"Kaynak teklif: {offer.OfferNumber}",
            offer.DeliveryDate is { } delivery ? $"Teslimat tarihi: {delivery:dd.MM.yyyy}" : null,
            offer.DistanceKm is { } km ? $"Mesafe: {km:0.#} km" : null,
            offer.DeliveryTerms.TrimOrNull() is { } dt ? $"Teslimat koşulları: {dt}" : null,
            offer.PaymentTerms.TrimOrNull() is { } pt ? $"Ödeme koşulları: {pt}" : null,
            offer.InternalNotes.TrimOrNull(),
        }.Where(s => s is not null));

        var contacts = new List<JobContactRequest>();
        if (offer.CustomerPhone.TrimOrNull() is { } phone)
            contacts.Add(new JobContactRequest { Role = "Müşteri yetkilisi", Name = offer.ContactPerson.TrimOrNull(), PhoneNumber = phone });

        return new CreateJobRequest
        {
            CustomerId = offer.CustomerId,
            JobDate = offer.LoadingDate ?? today,
            StoreName = string.Empty,
            VehicleType = offer.VehicleType,
            Stops =
            [
                new JobStopRequest { StopType = StopType.Pickup, Address = offer.LoadingPoint, Description = offer.LoadingAddress.TrimOrNull() },
                new JobStopRequest { StopType = StopType.Delivery, Address = offer.UnloadingPoint, Description = offer.UnloadingAddress.TrimOrNull() },
            ],
            Description = Truncate(description, 1000),
            Price = offer.NetAmount,
            Notes = Truncate(notes, 2000),
            Contacts = contacts,
            Currency = offer.Currency,
        };
    }

    public static string TransportTypeLabel(TransportType type) => type == TransportType.Partial ? "Parsiyel" : "Komple";

    private async Task<TransportOfferDetailDto> MutateAsync(Guid id, Action<TransportOffer> action, CancellationToken ct)
    {
        EnsureAdmin();
        var offer = await offers.Query().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw NotFoundException.For("Teklif");
        var before = offer.Status;
        action(offer);
        try
        {
            // Değişiklik yoksa (ör. zaten gönderilmiş teklif) kayıt yapılmaz.
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Teklif başka bir işlemle değiştirildi. Lütfen yenileyip tekrar deneyin.");
        }
        logger.LogInformation("Teklif durumu güncellendi. OfferId: {OfferId}, {From} -> {To}, ActorId: {ActorId}", id, before, offer.Status, currentUser.UserId);
        return await GetAsync(id, ct);
    }

    private async Task<TransportOffer> LoadDetailAsync(Guid id, CancellationToken ct) =>
        await offers.QueryAsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.History).ThenInclude(h => h.ChangedByUser)
            .Include(o => o.CreatedByUser)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw NotFoundException.For("Teklif");

    private async Task<TransportOfferDetailDto> ToDetailAsync(TransportOffer o, CancellationToken ct)
    {
        var today = calendar.Today;
        var labels = await vehicleTypes.GetLabelsAsync(ct);
        // Dönüştürülen iş sonradan silinmiş olsa da numarası gösterilir.
        var jobNumber = o.ConvertedJobId is { } jobId
            ? await jobs.QueryAsNoTracking().IgnoreQueryFilters().Where(j => j.Id == jobId).Select(j => j.JobNumber).FirstOrDefaultAsync(ct)
            : null;
        var status = o.EffectiveStatus(today);
        var editable = TransportOffer.EditableStatuses.Contains(o.Status);
        return new TransportOfferDetailDto
        {
            Id = o.Id,
            OfferNumber = o.OfferNumber,
            OfferDate = o.OfferDate,
            ValidUntil = o.ValidUntil,
            Status = status,
            StoredStatus = o.Status,
            CustomerId = o.CustomerId,
            CustomerName = o.CustomerName,
            ContactPerson = o.ContactPerson,
            CustomerPhone = o.CustomerPhone,
            CustomerEmail = o.CustomerEmail,
            CustomerAddress = o.CustomerAddress,
            LoadingPoint = o.LoadingPoint,
            LoadingAddress = o.LoadingAddress,
            UnloadingPoint = o.UnloadingPoint,
            UnloadingAddress = o.UnloadingAddress,
            TransportType = o.TransportType,
            VehicleType = o.VehicleType,
            VehicleTypeName = labels.TryGetValue(o.VehicleType, out var name) ? name : o.VehicleType,
            CargoDescription = o.CargoDescription,
            LoadingDate = o.LoadingDate,
            DeliveryDate = o.DeliveryDate,
            DistanceKm = o.DistanceKm,
            Items = o.Items.OrderBy(i => i.Sequence)
                .Select(i => new TransportOfferItemDto(i.Id, i.Sequence, i.Description, i.Unit, i.Quantity, i.UnitPrice, i.TotalPrice)).ToList(),
            SubTotal = o.SubTotal,
            DiscountAmount = o.DiscountAmount,
            VatRate = o.VatRate,
            VatAmount = o.VatAmount,
            TotalAmount = o.TotalAmount,
            Currency = o.Currency,
            PaymentTerms = o.PaymentTerms,
            DeliveryTerms = o.DeliveryTerms,
            CustomerNotes = o.CustomerNotes,
            InternalNotes = o.InternalNotes,
            SentAt = o.SentAt,
            AcceptedAt = o.AcceptedAt,
            RejectedAt = o.RejectedAt,
            RejectionReason = o.RejectionReason,
            CancelledAt = o.CancelledAt,
            CancellationReason = o.CancellationReason,
            ConvertedJobId = o.ConvertedJobId,
            ConvertedJobNumber = jobNumber,
            ConvertedAt = o.ConvertedAt,
            CopiedFromOfferId = o.CopiedFromOfferId,
            CreatedByUserId = o.CreatedByUserId,
            CreatedByUserName = o.CreatedByUser?.FullName,
            CreatedAt = o.CreatedAt,
            UpdatedAt = o.UpdatedAt,
            CanEdit = editable,
            CanSend = o.Status == TransportOfferStatus.Draft && status != TransportOfferStatus.Expired,
            CanAccept = editable && status != TransportOfferStatus.Expired,
            CanReject = editable,
            CanCancel = o.Status is not (TransportOfferStatus.Cancelled or TransportOfferStatus.Rejected) && !o.IsConverted,
            CanConvert = o.Status == TransportOfferStatus.Accepted && !o.IsConverted,
            CanDelete = !o.IsConverted && o.Status != TransportOfferStatus.Accepted,
            PdfFileName = PdfFileName(o.OfferNumber),
            History = o.History.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
                .Select(h => new TransportOfferHistoryDto(h.Id, h.OldStatus, h.NewStatus, h.Description, h.ChangedByUserId, h.ChangedByUser?.FullName, h.CreatedAt))
                .ToList(),
        };
    }

    public static string PdfFileName(string offerNumber) =>
        $"AKNA_Teklif_{string.Concat(offerNumber.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'))}.pdf";

    private static IQueryable<TransportOffer> ApplyFilters(IQueryable<TransportOffer> q, TransportOfferQuery query, DateOnly today, bool includeStatus)
    {
        if (query.CustomerId is { } customerId) q = q.Where(o => o.CustomerId == customerId);
        if (query.StartDate is { } start) q = q.Where(o => o.OfferDate >= start);
        if (query.EndDate is { } end) q = q.Where(o => o.OfferDate <= end);
        if (query.Search.TrimOrNull() is { } search)
        {
            var s = search.ToLower();
            q = q.Where(o => o.OfferNumber.ToLower().Contains(s) || o.CustomerName.ToLower().Contains(s)
                             || (o.ContactPerson != null && o.ContactPerson.ToLower().Contains(s))
                             || o.LoadingPoint.ToLower().Contains(s) || o.UnloadingPoint.ToLower().Contains(s));
        }
        if (includeStatus && query.Status is { Count: > 0 } statuses)
        {
            var wantExpired = statuses.Contains(TransportOfferStatus.Expired);
            var stored = statuses.Where(s => s != TransportOfferStatus.Expired).Distinct().ToList();
            q = q.Where(o =>
                (stored.Contains(o.Status) && !((o.Status == TransportOfferStatus.Draft || o.Status == TransportOfferStatus.Pending) && o.ValidUntil < today))
                || (wantExpired && (o.Status == TransportOfferStatus.Draft || o.Status == TransportOfferStatus.Pending) && o.ValidUntil < today));
        }
        return q;
    }

    private static IQueryable<TransportOffer> Sort(IQueryable<TransportOffer> q, TransportOfferQuery query) =>
        (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("totalamount", true) => q.OrderByDescending(o => o.TotalAmount).ThenByDescending(o => o.Id),
            ("totalamount", false) => q.OrderBy(o => o.TotalAmount).ThenBy(o => o.Id),
            ("validuntil", true) => q.OrderByDescending(o => o.ValidUntil).ThenByDescending(o => o.Id),
            ("validuntil", false) => q.OrderBy(o => o.ValidUntil).ThenBy(o => o.Id),
            ("offernumber", true) => q.OrderByDescending(o => o.OfferNumber),
            ("offernumber", false) => q.OrderBy(o => o.OfferNumber),
            ("customername", true) => q.OrderByDescending(o => o.CustomerName).ThenByDescending(o => o.Id),
            ("customername", false) => q.OrderBy(o => o.CustomerName).ThenBy(o => o.Id),
            (_, false) => q.OrderBy(o => o.OfferDate).ThenBy(o => o.CreatedAt),
            _ => q.OrderByDescending(o => o.OfferDate).ThenByDescending(o => o.CreatedAt),
        };

    /// <summary>Kayıtlı müşteri doğrulanır; yoksa teklifteki bilgilerle mevcut müşteri altyapısıyla yeni müşteri açılır.</summary>
    private async Task<Guid> ResolveCustomerAsync(TransportOfferRequest request, CancellationToken ct)
    {
        if (request.CustomerId is { } id)
        {
            await customerService.EnsureExistsAsync(id, ct);
            return id;
        }
        var c = request.Customer;
        var customer = customerService.AddWithoutSaving(new CustomerRequest
        {
            CompanyName = c.CompanyName.Trim(),
            ContactPerson = c.ContactPerson.TrimOrNull(),
            PhoneNumber = c.PhoneNumber.TrimOrNull(),
            Email = c.Email.TrimOrNull(),
            Address = c.Address.TrimOrNull(),
        });
        logger.LogInformation("Teklifle birlikte yeni müşteri oluşturuldu. CustomerId: {CustomerId}", customer.Id);
        return customer.Id;
    }

    private static void Apply(TransportOffer offer, TransportOfferRequest r)
    {
        offer.CustomerName = r.Customer.CompanyName.Trim();
        offer.ContactPerson = r.Customer.ContactPerson.TrimOrNull();
        offer.CustomerPhone = r.Customer.PhoneNumber.TrimOrNull();
        offer.CustomerEmail = r.Customer.Email.TrimOrNull();
        offer.CustomerAddress = r.Customer.Address.TrimOrNull();
        offer.LoadingPoint = r.LoadingPoint.Trim();
        offer.LoadingAddress = r.LoadingAddress.TrimOrNull();
        offer.UnloadingPoint = r.UnloadingPoint.Trim();
        offer.UnloadingAddress = r.UnloadingAddress.TrimOrNull();
        offer.TransportType = r.TransportType;
        offer.VehicleType = r.VehicleType.Trim();
        offer.CargoDescription = r.CargoDescription.TrimOrNull();
        offer.LoadingDate = r.LoadingDate;
        offer.DeliveryDate = r.DeliveryDate;
        offer.DistanceKm = r.DistanceKm is { } km ? Math.Round(km, 1, MidpointRounding.AwayFromZero) : null;
        offer.Currency = r.Currency ?? offer.Currency;
        offer.PaymentTerms = r.PaymentTerms.TrimOrNull();
        offer.DeliveryTerms = r.DeliveryTerms.TrimOrNull();
        offer.CustomerNotes = r.CustomerNotes.TrimOrNull();
        offer.InternalNotes = r.InternalNotes.TrimOrNull();
    }

    private static IReadOnlyList<TransportOfferItemInput> ResolveItems(TransportOfferRequest r) =>
        r.Items.Select(i => new TransportOfferItemInput(i.Description, i.Quantity, i.UnitPrice, i.Unit)).ToList();

    private decimal ResolveVatRate(TransportOfferRequest r) => r.VatRate ?? appOptions.Value.DefaultVatRate;

    private static (DateOnly OfferDate, DateOnly ValidUntil) ResolveDates(TransportOfferRequest r, DateOnly today, DateOnly? current = null)
    {
        var offerDate = r.OfferDate ?? current ?? today;
        var validUntil = r.ValidUntil ?? offerDate.AddDays(TransportOffer.DefaultValidityDays);
        if (validUntil < offerDate)
            throw new BusinessValidationException("Geçerlilik tarihi teklif tarihinden önce olamaz.", [new ErrorDetail("validUntil", "Geçerlilik tarihini kontrol edin.")]);
        return (offerDate, validUntil);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin) throw new ForbiddenException();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
}
