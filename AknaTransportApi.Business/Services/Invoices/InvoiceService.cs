using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Services.Invoices;

public sealed class InvoiceService(
    IRepository<Invoice> invoices,
    IRepository<InvoiceItem> invoiceItems,
    IRepository<TransportJob> jobs,
    IRepository<Customer> customers,
    IRepository<EmailLog> emailLogs,
    IRepository<OutboxMessage> outbox,
    IUnitOfWork unitOfWork,
    INumberGenerator numberGenerator,
    IEArchiveInvoiceService eArchive,
    IDraftInvoicePdfGenerator draftPdf,
    IJobReportPdfGenerator jobReportPdf,
    IFileStorage fileStorage,
    ICurrentUser currentUser,
    IOptions<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    public async Task<PagedResult<InvoiceListItemDto>> ListAsync(InvoiceQuery query, CancellationToken ct)
    {
        var q = invoices.QueryAsNoTracking();
        if (query.CustomerId is { } customerId) q = q.Where(i => i.CustomerId == customerId);
        if (query.Status is { } status) q = q.Where(i => i.Status == status);
        if (query.StartDate is { } start) q = q.Where(i => i.InvoiceDate >= start);
        if (query.EndDate is { } end) q = q.Where(i => i.InvoiceDate <= end);
        if (query.Search.TrimOrNull() is { } search)
        {
            var s = search.ToLower();
            q = q.Where(i => i.InvoiceNumber.ToLower().Contains(s) || (i.EArchiveInvoiceNumber != null && i.EArchiveInvoiceNumber.ToLower().Contains(s))
                             || i.Customer!.CompanyName.ToLower().Contains(s));
        }

        q = (query.SortBy?.ToLowerInvariant(), query.Descending) switch
        {
            ("grandtotal", true) => q.OrderByDescending(i => i.GrandTotal),
            ("grandtotal", false) => q.OrderBy(i => i.GrandTotal),
            ("invoicenumber", true) => q.OrderByDescending(i => i.InvoiceNumber),
            ("invoicenumber", false) => q.OrderBy(i => i.InvoiceNumber),
            (_, false) => q.OrderBy(i => i.InvoiceDate).ThenBy(i => i.CreatedAt),
            _ => q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.CreatedAt),
        };
        return await q.Select(InvoiceMappings.ListProjection).ToPagedResultAsync(query, ct);
    }

    public async Task<InvoiceDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var header = await invoices.QueryAsNoTracking().Where(i => i.Id == id).Select(i => new
        {
            Invoice = i,
            CustomerName = i.Customer!.CompanyName,
            CustomerEmail = i.Customer.Email,
        }).FirstOrDefaultAsync(ct) ?? throw NotFoundException.For("Fatura");

        var items = await invoiceItems.QueryAsNoTracking().Where(x => x.InvoiceId == id)
            .OrderBy(x => x.Job!.JobDate).ThenBy(x => x.Job!.JobNumber)
            .Select(x => new InvoiceItemDto(x.Id, x.JobId, x.Job!.JobNumber, x.Job.JobDate, x.Job.StoreName, x.Description, x.Quantity, x.UnitPrice, x.VatRate, x.LineTotal))
            .ToListAsync(ct);

        var email = await emailLogs.QueryAsNoTracking().Where(e => e.InvoiceId == id && e.Type == EmailType.InvoiceDelivery)
            .Select(e => new InvoiceEmailDto(e.Id, e.RecipientEmail, e.Subject, e.Status, e.AttemptCount, e.LastError, e.SentAt, e.CreatedAt))
            .FirstOrDefaultAsync(ct);

        var i = header.Invoice;
        var withholding = WithholdingTypes.Find(i.WithholdingCode);
        var requiresSms = i.Status == InvoiceStatus.Pending && i.EArchiveReference is not null && eArchive.IsConfigured && eArchive.SupportsSmsSigning;
        return new InvoiceDetailDto(i.Id, i.InvoiceNumber, i.CustomerId, header.CustomerName, header.CustomerEmail, i.InvoiceDate, i.SubTotal, i.VatRate,
            i.VatAmount, i.GrandTotal, i.Currency, i.Status, i.Status == InvoiceStatus.Issued, i.Notes, i.EArchiveProvider, i.EArchiveReference,
            i.EArchiveInvoiceNumber, i.EArchiveInvoiceUrl, i.LastError, i.CreatedByUserId, i.IssuedAt, i.CancelledAt, i.CreatedAt, i.UpdatedAt, items, email,
            i.WithholdingCode, withholding?.Name, i.WithholdingRate, i.WithholdingAmount, i.PayableAmount, i.AmountInWordsText,
            i.Iban is null ? null : Iban.Format(i.Iban), i.Iban is null ? null : appOptions.Value.CompanyBankName.TrimOrNull(), ComposeDocumentNote(i), requiresSms);
    }

    public IReadOnlyList<WithholdingTypeDto> GetWithholdingTypes() =>
        WithholdingTypes.All.Select(w => new WithholdingTypeDto(w.Code, w.Rate, w.Fraction, w.Name)).ToList();

    /// <summary>
    /// Faturanın açıklama metni: yazıyla ödenecek tutar, IBAN ve kullanıcı notu. e-Arşiv'de "not" alanına,
    /// PDF'te açıklama bölümüne yazılır.
    /// </summary>
    private string ComposeDocumentNote(Invoice invoice)
    {
        var lines = new List<string> { invoice.AmountInWordsText };
        if (WithholdingTypes.Find(invoice.WithholdingCode) is { } w)
            lines.Add($"KDV tevkifatı: {w.Code} {w.Name} ({w.Fraction}) {invoice.WithholdingAmount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))} {invoice.Currency}");
        if (invoice.Iban is { } iban)
        {
            var bank = appOptions.Value.CompanyBankName.TrimOrNull();
            lines.Add(bank is null ? $"IBAN: {Iban.Format(iban)}" : $"IBAN: {Iban.Format(iban)} ({bank})");
        }
        if (invoice.Notes.TrimOrNull() is { } notes) lines.Add(notes);
        return string.Join("\n", lines);
    }

    private WithholdingType? ResolveWithholding(string? code) => WithholdingTypes.Find(code)
        ?? (string.IsNullOrWhiteSpace(code) ? null : throw new BusinessValidationException("Geçersiz tevkifat kodu."));

    private string? ResolveIban(string? requested)
    {
        var iban = Iban.Normalize(requested) ?? Iban.Normalize(appOptions.Value.CompanyIban);
        if (iban is not null && !Iban.IsValid(iban))
            throw new BusinessValidationException(requested is null ? "App:CompanyIban ayarındaki IBAN geçersiz." : "IBAN geçersiz.");
        return iban;
    }

    public async Task<IReadOnlyList<JobDto>> GetJobsAsync(Guid id, CancellationToken ct)
    {
        if (!await invoices.QueryAsNoTracking().AnyAsync(i => i.Id == id, ct)) throw NotFoundException.For("Fatura");
        var jobIds = invoiceItems.QueryAsNoTracking().Where(x => x.InvoiceId == id).Select(x => x.JobId);
        return await jobs.QueryAsNoTracking().Where(j => jobIds.Contains(j.Id)).OrderBy(j => j.JobDate).ThenBy(j => j.JobNumber)
            .Select(JobMappings.Projection).ToListAsync(ct);
    }

    public async Task<InvoiceDetailDto> CreateAsync(CreateInvoiceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var selected = await LoadInvoiceableJobsAsync(request.JobIds, currentInvoiceId: null, ct);
        var customer = await LoadCustomerForInvoiceAsync(selected[0].CustomerId, ct);
        var invoiceDate = request.InvoiceDate ?? DateOnly.FromDateTime(now);
        var withholding = request.NoWithholding ? null : ResolveWithholding(request.WithholdingCode ?? appOptions.Value.DefaultWithholdingCode);
        var iban = ResolveIban(request.Iban);

        var invoiceId = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var invoice = new Invoice
            {
                CustomerId = customer.Id,
                InvoiceDate = invoiceDate,
                Currency = selected[0].Currency,
                Notes = request.Notes.TrimOrNull(),
                CreatedByUserId = currentUser.UserId,
                InvoiceNumber = await numberGenerator.NextInvoiceNumberAsync(invoiceDate, token),
                Iban = iban,
            };
            foreach (var job in selected) invoice.Items.Add(CreateItem(invoice.Id, job));
            invoice.Recalculate(request.VatRate ?? appOptions.Value.DefaultVatRate, withholding);
            invoices.Add(invoice);
            await unitOfWork.SaveChangesAsync(token);
            await ClaimJobsAsync(invoice.Id, request.JobIds, now, token);
            return invoice.Id;
        }, ct);

        logger.LogInformation("Taslak fatura oluşturuldu. InvoiceId: {InvoiceId}, JobCount: {Count}", invoiceId, selected.Count);
        return await GetAsync(invoiceId, ct);
    }

    public async Task<InvoiceDetailDto> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invoice = await invoices.Query().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw NotFoundException.For("Fatura");
        invoice.EnsureEditable();

        var selected = await LoadInvoiceableJobsAsync(request.JobIds, currentInvoiceId: id, ct);
        if (selected.Any(j => j.CustomerId != invoice.CustomerId))
            throw new BusinessValidationException("Faturadaki tüm işler aynı müşteriye ait olmalıdır.");
        await LoadCustomerForInvoiceAsync(invoice.CustomerId, ct);
        var withholding = ResolveWithholding(request.WithholdingCode);
        var iban = ResolveIban(request.Iban);

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // Önce eski işler serbest bırakılır, sonra yeni liste koşullu olarak bağlanır.
            await jobs.Query().Where(j => j.InvoiceId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.InvoiceId, (Guid?)null).SetProperty(j => j.UpdatedAt, now), token);

            foreach (var item in invoice.Items.ToList()) invoiceItems.Remove(item);
            invoice.Items.Clear();
            foreach (var job in selected)
            {
                var item = CreateItem(invoice.Id, job);
                invoice.Items.Add(item);
                invoiceItems.Add(item);
            }
            invoice.InvoiceDate = request.InvoiceDate;
            invoice.Notes = request.Notes.TrimOrNull();
            invoice.Iban = iban;
            invoice.Recalculate(request.VatRate, withholding);
            await unitOfWork.SaveChangesAsync(token);
            await ClaimJobsAsync(invoice.Id, request.JobIds, now, token);
            return true;
        }, ct);

        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDetailDto> IssueAsync(Guid id, CancellationToken ct)
    {
        if (!eArchive.IsConfigured)
            throw new IntegrationUnavailableException(
                "e-Arşiv sağlayıcısı yapılandırılmamış. Fatura taslak olarak kaldı; gerçek fatura kesilemez.");

        var invoice = await invoices.Query().Include(i => i.Items).ThenInclude(x => x.Job)
                          .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw NotFoundException.For("Fatura");
        invoice.EnsureEditable();
        var customer = await LoadCustomerForInvoiceAsync(invoice.CustomerId, ct);

        // Kesimden önce faturadaki her işin hâlâ bu faturaya bağlı ve tamamlanmış olduğu doğrulanır.
        if (invoice.Items.Count == 0 || invoice.Items.Any(x => x.Job is null || x.Job.InvoiceId != invoice.Id || x.Job.Status != JobStatus.Completed))
            throw new ConflictException("Faturadaki işlerden biri artık bu faturaya bağlı değil veya tamamlanmış durumda değil. Faturayı güncelleyin.");

        // Pending durumu kaydedilir; ConcurrencyStamp sayesinde aynı fatura eşzamanlı iki kez gönderilemez.
        invoice.MarkPending(eArchive.ProviderName!);
        await unitOfWork.SaveChangesAsync(ct);

        EArchiveSendResult result;
        try
        {
            result = await eArchive.SendInvoiceAsync(BuildRequest(invoice, customer), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "e-Arşiv gönderimi başarısız. InvoiceId: {InvoiceId}", id);
            invoice.MarkFailed($"e-Arşiv sağlayıcısına gönderim başarısız: {ex.Message}");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return await GetAsync(id, CancellationToken.None);
        }

        await ApplySendResultAsync(invoice, customer, result);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        return await GetAsync(id, CancellationToken.None);
    }

    public async Task<InvoiceDetailDto> CancelAsync(Guid id, CancelInvoiceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invoice = await invoices.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Fatura");

        if (invoice.Status == InvoiceStatus.Issued)
        {
            if (!eArchive.IsConfigured) throw new IntegrationUnavailableException("e-Arşiv sağlayıcısı yapılandırılmamış; kesilmiş fatura iptal edilemez.");
            var result = await eArchive.CancelAsync(Key(invoice), request.Reason.TrimOrNull() ?? "İptal", ct);
            if (!result.Success) throw new ConflictException($"e-Arşiv iptali başarısız: {result.ErrorMessage}");
        }

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            invoice.Cancel(now);
            if (request.Reason.TrimOrNull() is { } reason) invoice.Notes = string.IsNullOrEmpty(invoice.Notes) ? $"İptal nedeni: {reason}" : $"{invoice.Notes}\nİptal nedeni: {reason}";
            await unitOfWork.SaveChangesAsync(token);
            // İptal edilen faturanın işleri yeniden faturalanabilir hale gelir; kalemler geçmiş için korunur.
            await jobs.Query().Where(j => j.InvoiceId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.InvoiceId, (Guid?)null).SetProperty(j => j.UpdatedAt, now), token);
            return true;
        }, ct);

        logger.LogInformation("Fatura iptal edildi. InvoiceId: {InvoiceId}", id);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDetailDto> ResendEmailAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var invoice = await invoices.QueryAsNoTracking().Where(i => i.Id == id).Select(i => new { i.Status }).FirstOrDefaultAsync(ct)
                      ?? throw NotFoundException.For("Fatura");
        if (invoice.Status != InvoiceStatus.Issued) throw new ConflictException("Yalnızca kesilmiş faturaların e-postası gönderilebilir.");

        var log = await emailLogs.Query().FirstOrDefaultAsync(e => e.InvoiceId == id && e.Type == EmailType.InvoiceDelivery, ct);
        if (log is { Status: EmailStatus.Sent }) throw new ConflictException("Bu faturanın e-postası zaten gönderildi.");
        if (log is { Status: EmailStatus.Pending }) throw new ConflictException("Bu faturanın e-postası zaten gönderim kuyruğunda.");

        if (log is null)
        {
            var customerId = await invoices.QueryAsNoTracking().Where(i => i.Id == id).Select(i => i.CustomerId).FirstAsync(ct);
            var customer = await LoadCustomerForInvoiceAsync(customerId, ct);
            log = new EmailLog
            {
                InvoiceId = id,
                Type = EmailType.InvoiceDelivery,
                RecipientEmail = customer.Email!.Trim(),
                Subject = EmailTemplates.InvoiceSubject,
                CreatedAt = now,
            };
            emailLogs.Add(log);
        }

        log.Status = EmailStatus.Pending;
        log.LastError = null;
        OutboxWriter.Enqueue(outbox, OutboxMessageTypes.InvoiceEmail, new InvoiceEmailPayload(id, log.Id), now);
        await unitOfWork.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<FileDownload> GetPdfAsync(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.QueryAsNoTracking().Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == id, ct)
                      ?? throw NotFoundException.For("Fatura");

        if (invoice.Status == InvoiceStatus.Issued)
        {
            var bytes = await GetOfficialPdfAsync(id, ct);
            return new FileDownload(bytes, $"Fatura-{SafeName(invoice.EArchiveInvoiceNumber ?? invoice.InvoiceNumber)}.pdf");
        }

        // Kesilmemiş faturalar için "resmi fatura değildir" ibareli önizleme üretilir.
        var pdf = await RenderPdfAsync(invoice, official: false, ct);
        return new FileDownload(pdf, $"TASLAK-{SafeName(invoice.InvoiceNumber)}.pdf");
    }

    private async Task<byte[]> RenderPdfAsync(Invoice invoice, bool official, CancellationToken ct)
    {
        var customer = invoice.Customer ?? await customers.QueryAsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId, ct);
        var lines = await invoiceItems.QueryAsNoTracking().Where(x => x.InvoiceId == invoice.Id).OrderBy(x => x.Job!.JobDate)
            .Select(x => new DraftInvoiceLine(x.Description, x.Quantity, x.UnitPrice, x.LineTotal)).ToListAsync(ct);
        var withholding = WithholdingTypes.Find(invoice.WithholdingCode);
        return draftPdf.Generate(new DraftInvoiceDocument(invoice.InvoiceNumber, invoice.InvoiceDate, invoice.Status, customer.CompanyName, customer.TaxOffice,
            customer.TaxNumber, customer.Address, lines, invoice.SubTotal, invoice.VatRate, invoice.VatAmount, invoice.GrandTotal, invoice.Currency,
            clock.GetUtcNow().UtcDateTime)
        {
            Official = official,
            EArchiveInvoiceNumber = invoice.EArchiveInvoiceNumber,
            Ettn = invoice.EArchiveReference,
            WithholdingLabel = withholding is null ? null : $"KDV Tevkifatı ({withholding.Fraction}, {withholding.Code})",
            WithholdingAmount = invoice.WithholdingAmount,
            PayableAmount = invoice.PayableAmount,
            AmountInWords = invoice.AmountInWordsText,
            Iban = invoice.Iban is null ? null : Iban.Format(invoice.Iban),
            BankName = invoice.Iban is null ? null : appOptions.Value.CompanyBankName.TrimOrNull(),
            Notes = invoice.Notes,
            CompanyName = appOptions.Value.CompanyName,
        });
    }

    public async Task<byte[]> GetOfficialPdfAsync(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Fatura");
        if (invoice.Status != InvoiceStatus.Issued || string.IsNullOrEmpty(invoice.EArchiveReference))
            throw new ConflictException("Resmi fatura PDF'i yalnızca kesilmiş faturalar için mevcuttur.");

        if (invoice.InvoicePdfPath is { } path && await fileStorage.ReadAsync(path, ct) is { } cached) return cached;

        // GİB portalı PDF vermez: kesilmiş faturanın bilgileri (GİB numarası, ETTN) ile e-Arşiv fatura PDF'i üretilir.
        // Resmi görüntü /earchive/view ile portaldan alınabilir.
        if (!eArchive.IsConfigured || !eArchive.SupportsPdf)
        {
            if (invoice.EArchiveProvider is null) throw new IntegrationUnavailableException("e-Arşiv sağlayıcısı yapılandırılmamış; resmi PDF alınamıyor.");
            return await RenderPdfAsync(invoice, official: true, ct);
        }
        var pdf = await eArchive.GetPdfAsync(Key(invoice), ct);
        if (pdf.Length == 0) throw new IntegrationUnavailableException("e-Arşiv sağlayıcısı boş PDF döndürdü.");
        invoice.InvoicePdfPath = await fileStorage.SaveAsync("invoices", $"{invoice.Id:N}.pdf", pdf, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return pdf;
    }

    public async Task<JobReportDocument> BuildJobReportAsync(Guid id, CancellationToken ct)
    {
        var header = await invoices.QueryAsNoTracking().Where(i => i.Id == id)
                         .Select(i => new { i.InvoiceNumber, i.EArchiveInvoiceNumber, i.Currency, CustomerName = i.Customer!.CompanyName })
                         .FirstOrDefaultAsync(ct) ?? throw NotFoundException.For("Fatura");

        // Yalnızca bu faturanın kalemlerindeki işler: müşteriye başka işlerin kaydı gitmez.
        var rows = await invoiceItems.QueryAsNoTracking().Where(x => x.InvoiceId == id)
            .OrderBy(x => x.Job!.JobDate).ThenBy(x => x.Job!.JobNumber)
            .Select(x => new JobReportRow(x.Job!.JobDate, x.Job.StoreName, x.Job.VehicleType, (x.Job.RouteDescription == "" ? x.Job.Description : x.Job.Description == "" ? x.Job.RouteDescription : x.Job.RouteDescription + " (" + x.Job.Description + ")"), x.Job.Price, null))
            .ToListAsync(ct);

        return new JobReportDocument(
            "NAKLİYE İŞLERİ DÖKÜMÜ",
            rows.Count > 0 ? rows.Min(r => r.Date) : null,
            rows.Count > 0 ? rows.Max(r => r.Date) : null,
            rows,
            rows.Sum(r => r.Price),
            header.Currency,
            clock.GetUtcNow().UtcDateTime,
            header.CustomerName,
            $"Fatura No: {header.EArchiveInvoiceNumber ?? header.InvoiceNumber}");
    }

    public async Task<FileDownload> GetJobReportPdfAsync(Guid id, CancellationToken ct)
    {
        var report = await BuildJobReportAsync(id, ct);
        var number = await invoices.QueryAsNoTracking().Where(i => i.Id == id).Select(i => i.EArchiveInvoiceNumber ?? i.InvoiceNumber).FirstAsync(ct);
        return new FileDownload(jobReportPdf.Generate(report), $"Nakliye-Dokumu-{SafeName(number)}.pdf");
    }

    public async Task<int> SyncPendingAsync(CancellationToken ct)
    {
        if (!eArchive.IsConfigured) return 0;
        var threshold = clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        var pending = await invoices.Query()
            .Where(i => i.Status == InvoiceStatus.Pending && i.EArchiveReference != null && (i.UpdatedAt ?? i.CreatedAt) < threshold)
            .OrderBy(i => i.UpdatedAt).Take(50).ToListAsync(ct);

        var changed = 0;
        foreach (var invoice in pending)
        {
            try
            {
                var status = await eArchive.GetStatusAsync(Key(invoice), ct);
                var customer = await customers.QueryAsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId, ct);
                var before = invoice.Status;
                await ApplySendResultAsync(invoice, customer,
                    new EArchiveSendResult(status.Status != EArchiveDocumentStatus.Rejected, status.Status, invoice.EArchiveReference, status.InvoiceNumber, status.InvoiceUrl, status.ErrorMessage));
                await unitOfWork.SaveChangesAsync(ct);
                if (invoice.Status != before) changed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "e-Arşiv durum sorgusu başarısız. InvoiceId: {InvoiceId}", invoice.Id);
            }
        }
        return changed;
    }

    /// <summary>
    /// Sağlayıcı yanıtını faturaya uygular. Issued olduğunda e-posta kaydı ve outbox mesajı
    /// fatura durumuyla aynı SaveChanges içinde yazılır (transactional outbox).
    /// </summary>
    private Task ApplySendResultAsync(Invoice invoice, Customer customer, EArchiveSendResult result)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!result.Accepted || result.Status == EArchiveDocumentStatus.Rejected)
        {
            invoice.MarkFailed(result.ErrorMessage ?? "e-Arşiv sağlayıcısı faturayı reddetti.");
            logger.LogWarning("e-Arşiv faturası reddedildi. InvoiceId: {InvoiceId}", invoice.Id);
            return Task.CompletedTask;
        }

        if (result.Status == EArchiveDocumentStatus.Issued && !string.IsNullOrWhiteSpace(result.Reference))
        {
            invoice.MarkIssued(result.Reference, result.InvoiceNumber, result.InvoiceUrl, now);
            EnqueueInvoiceEmail(invoice, customer, now);
            logger.LogInformation("e-Arşiv faturası kesildi. InvoiceId: {InvoiceId}", invoice.Id);
        }
        else
        {
            // Sağlayıcı işlemde: referans saklanır, durum arka planda senkronize edilir.
            invoice.SetProviderReference(result.Reference, result.InvoiceNumber, result.InvoiceUrl);
        }
        return Task.CompletedTask;
    }

    public async Task<EArchiveSmsChallengeDto> SendSignSmsAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadPendingPortalInvoiceAsync(id, ct);
        var challenge = await eArchive.StartSmsSigningAsync(Key(invoice), ct);
        logger.LogInformation("e-Arşiv imza SMS'i gönderildi. InvoiceId: {InvoiceId}", id);
        return new EArchiveSmsChallengeDto(challenge.OperationId, challenge.MaskedPhone);
    }

    public async Task<InvoiceDetailDto> SignAsync(Guid id, SignInvoiceRequest request, CancellationToken ct)
    {
        var invoice = await LoadPendingPortalInvoiceAsync(id, ct);
        var customer = await LoadCustomerForInvoiceAsync(invoice.CustomerId, ct);
        var result = await eArchive.CompleteSmsSigningAsync(Key(invoice), request.OperationId.Trim(), request.Code.Trim(), ct);

        switch (result.Status)
        {
            case EArchiveDocumentStatus.Issued:
                await ApplySendResultAsync(invoice, customer,
                    new EArchiveSendResult(true, EArchiveDocumentStatus.Issued, invoice.EArchiveReference, result.InvoiceNumber, result.InvoiceUrl, null));
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return await GetAsync(id, CancellationToken.None);
            case EArchiveDocumentStatus.Rejected or EArchiveDocumentStatus.Cancelled:
                invoice.MarkFailed(result.ErrorMessage ?? "e-Arşiv taslağı portalda bulunamadı.");
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                return await GetAsync(id, CancellationToken.None);
            default:
                // İmza tamamlanmadı: fatura bekler durumda kalır, kesildi olarak işaretlenmez.
                throw new BusinessValidationException(result.ErrorMessage ?? "Fatura imzalanamadı.", [new ErrorDetail("code", result.ErrorMessage ?? "Fatura imzalanamadı.")]);
        }
    }

    public async Task<InvoiceDetailDto> DiscardEArchiveDraftAsync(Guid id, CancellationToken ct)
    {
        var invoice = await LoadPendingPortalInvoiceAsync(id, ct);
        var result = await eArchive.DiscardDraftAsync(Key(invoice), "AKNA: taslak geri alındı", ct);
        if (!result.Success) throw new ConflictException(result.ErrorMessage ?? "Portal taslağı silinemedi.");
        invoice.RevertToDraft();
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("e-Arşiv portal taslağı silindi, fatura taslağa döndü. InvoiceId: {InvoiceId}", id);
        return await GetAsync(id, CancellationToken.None);
    }

    public async Task<FileDownload> GetEArchiveViewAsync(Guid id, CancellationToken ct)
    {
        var invoice = await invoices.QueryAsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw NotFoundException.For("Fatura");
        if (invoice.EArchiveReference is null || invoice.Status is not (InvoiceStatus.Pending or InvoiceStatus.Issued))
            throw new ConflictException("Bu fatura için e-Arşiv belgesi yok.");
        if (!eArchive.IsConfigured) throw new IntegrationUnavailableException("e-Arşiv sağlayıcısı yapılandırılmamış.");
        var html = await eArchive.GetHtmlAsync(Key(invoice), signed: invoice.Status == InvoiceStatus.Issued, ct)
                   ?? throw new IntegrationUnavailableException($"{eArchive.ProviderName} belge görüntüsü sağlamıyor; PDF'i kullanın.");
        return new FileDownload(System.Text.Encoding.UTF8.GetBytes(html), $"e-Arsiv-{SafeName(invoice.EArchiveInvoiceNumber ?? invoice.InvoiceNumber)}.html", "text/html; charset=utf-8");
    }

    private async Task<Invoice> LoadPendingPortalInvoiceAsync(Guid id, CancellationToken ct)
    {
        if (!eArchive.IsConfigured) throw new IntegrationUnavailableException("e-Arşiv sağlayıcısı yapılandırılmamış.");
        if (!eArchive.SupportsSmsSigning) throw new ConflictException($"{eArchive.ProviderName} sağlayıcısı SMS ile imza kullanmıyor.");
        var invoice = await invoices.Query().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw NotFoundException.For("Fatura");
        if (invoice.Status != InvoiceStatus.Pending || invoice.EArchiveReference is null)
            throw new ConflictException("Fatura imza bekleyen bir e-Arşiv taslağı değil.");
        return invoice;
    }

    private static EArchiveDocumentKey Key(Invoice invoice) =>
        new(invoice.EArchiveReference ?? throw new InvalidInvoiceStateException("e-Arşiv referansı yok."), invoice.InvoiceDate);

    private void EnqueueInvoiceEmail(Invoice invoice, Customer customer, DateTime now)
    {
        var log = new EmailLog
        {
            InvoiceId = invoice.Id,
            Type = EmailType.InvoiceDelivery,
            RecipientEmail = customer.Email!.Trim(),
            Subject = EmailTemplates.InvoiceSubject,
            Status = EmailStatus.Pending,
            CreatedAt = now,
        };
        emailLogs.Add(log);
        OutboxWriter.Enqueue(outbox, OutboxMessageTypes.InvoiceEmail, new InvoiceEmailPayload(invoice.Id, log.Id), now);
    }

    private async Task<List<TransportJob>> LoadInvoiceableJobsAsync(List<Guid> jobIds, Guid? currentInvoiceId, CancellationToken ct)
    {
        var selected = await jobs.QueryAsNoTracking().Where(j => jobIds.Contains(j.Id)).ToListAsync(ct);
        var errors = new List<ErrorDetail>();
        foreach (var missing in jobIds.Except(selected.Select(j => j.Id))) errors.Add(new ErrorDetail("jobIds", $"İş bulunamadı: {missing}"));
        foreach (var job in selected)
        {
            if (job.Status != JobStatus.Completed) errors.Add(new ErrorDetail("jobIds", $"{job.JobNumber} tamamlanmadığı için faturalanamaz."));
            if (job.InvoiceId is not null && job.InvoiceId != currentInvoiceId) errors.Add(new ErrorDetail("jobIds", $"{job.JobNumber} zaten başka bir faturada."));
        }
        if (selected.Select(j => j.CustomerId).Distinct().Count() > 1) errors.Add(new ErrorDetail("jobIds", "Seçilen işler aynı müşteriye ait olmalıdır."));
        if (selected.Select(j => j.Currency).Distinct().Count() > 1) errors.Add(new ErrorDetail("jobIds", "Seçilen işler aynı para biriminde olmalıdır."));

        if (errors.Count > 0)
        {
            if (errors.Any(e => e.Message.Contains("zaten başka bir faturada", StringComparison.Ordinal)))
                throw new ConflictException(string.Join(" ", errors.Select(e => e.Message)));
            throw new BusinessValidationException("Seçilen işler faturalanamaz.", errors);
        }
        return selected;
    }

    private async Task<Customer> LoadCustomerForInvoiceAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await customers.QueryAsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw NotFoundException.For("Müşteri");
        if (!Text.IsValidEmail(customer.Email))
            throw new BusinessValidationException("Müşterinin kayıtlı geçerli bir e-posta adresi yok. Fatura ve döküm e-postası gönderilebilmesi için müşteri kaydını güncelleyin.",
                [new ErrorDetail("customer.email", "Geçerli bir e-posta adresi gerekli.")]);
        return customer;
    }

    /// <summary>
    /// İşleri faturaya koşullu olarak bağlar. Yalnızca InvoiceId'si boş olan satırlar güncellenir;
    /// eşzamanlı iki istek aynı işi alamaz, etkilenen satır sayısı tutmazsa transaction geri alınır.
    /// </summary>
    private async Task ClaimJobsAsync(Guid invoiceId, List<Guid> jobIds, DateTime now, CancellationToken ct)
    {
        var affected = await jobs.Query()
            .Where(j => jobIds.Contains(j.Id) && j.InvoiceId == null && j.Status == JobStatus.Completed)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.InvoiceId, (Guid?)invoiceId).SetProperty(j => j.UpdatedAt, now), ct);
        if (affected != jobIds.Count) throw new ConflictException("Seçilen işlerden biri veya birkaçı başka bir faturaya eklenmiş.");
    }

    private static InvoiceItem CreateItem(Guid invoiceId, TransportJob job) => new()
    {
        InvoiceId = invoiceId,
        JobId = job.Id,
        Description = Truncate($"{job.JobDate:dd.MM.yyyy} {job.StoreName} - {JobMappings.ComposeText(job.RouteDescription, job.Description)} ({job.JobNumber})", 500),
        Quantity = 1,
        UnitPrice = job.Price,
    };

    private EArchiveInvoiceRequest BuildRequest(Invoice invoice, Customer customer) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.InvoiceDate,
        invoice.Currency,
        new EArchiveParty(customer.CompanyName, customer.TaxNumber, customer.TaxOffice, customer.Address, customer.Email, customer.PhoneNumber),
        invoice.Items.Select(i => new EArchiveLine(i.Description, i.Quantity, i.UnitPrice, i.VatRate, i.LineTotal, Money.CalculateVat(i.LineTotal, i.VatRate))).ToList(),
        invoice.SubTotal,
        invoice.VatAmount,
        invoice.GrandTotal,
        ComposeDocumentNote(invoice),
        invoice.WithholdingCode,
        invoice.WithholdingRate,
        invoice.WithholdingAmount,
        invoice.PayableAmount);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string SafeName(string value) => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_'));
}
