using AknaTransport.Business.Features.Invoices;
using AknaTransport.Business.Features.Locations;
using AknaTransport.Business.Features.Outbox;
using AknaTransport.Business.Options;
using AknaTransport.Domain.Entities;
using AknaTransport.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransport.Infrastructure.BackgroundJobs;

public sealed class BackgroundJobOptions
{
    public const string SectionName = "BackgroundJobs";

    /// <summary>false ise arka plan işleyicileri çalışmaz (ör. testler, salt API örnekleri).</summary>
    public bool Enabled { get; set; } = true;
    public int EArchiveSyncIntervalMinutes { get; set; } = 5;
    public int RetentionIntervalHours { get; set; } = 24;
}

/// <summary>Periyodik arka plan işlerinin ortak iskeleti: her turda yeni DI scope açar, hataları loglar.</summary>
public abstract class PeriodicWorker(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, ILogger logger) : BackgroundService
{
    protected BackgroundJobOptions JobOptions => options.Value;

    protected abstract TimeSpan Interval { get; }
    protected virtual TimeSpan InitialDelay => TimeSpan.FromSeconds(5);
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Worker} çalışırken hata oluştu.", GetType().Name);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // uygulama kapanıyor
        }
    }
}

/// <summary>Outbox mesajlarını (fatura e-postası, şifre e-postası, SMS) işler.</summary>
public sealed class OutboxWorker(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, IOptions<OutboxOptions> outboxOptions, ILogger<OutboxWorker> logger)
    : PeriodicWorker(scopeFactory, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(1, outboxOptions.Value.PollIntervalSeconds));

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var processor = services.GetRequiredService<IOutboxProcessor>();
        // Kuyruk boşalana kadar (veya parti dolmayana kadar) işlemeye devam et.
        while (await processor.ProcessPendingAsync(ct) >= outboxOptions.Value.BatchSize && !ct.IsCancellationRequested) { }
    }
}

/// <summary>Sağlayıcıda işlemde (Pending) kalan e-Arşiv faturalarının durumunu sorgular.</summary>
public sealed class EArchiveStatusSyncWorker(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, ILogger<EArchiveStatusSyncWorker> logger)
    : PeriodicWorker(scopeFactory, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(Math.Max(1, JobOptions.EArchiveSyncIntervalMinutes));

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var changed = await services.GetRequiredService<IInvoiceService>().SyncPendingAsync(ct);
        if (changed > 0) logger.LogInformation("{Count} e-Arşiv faturasının durumu güncellendi.", changed);
    }
}

/// <summary>Konum geçmişi saklama politikası ve süresi dolmuş token temizliği.</summary>
public sealed class DataRetentionWorker(IServiceScopeFactory scopeFactory, IOptions<BackgroundJobOptions> options, TimeProvider clock, ILogger<DataRetentionWorker> logger)
    : PeriodicWorker(scopeFactory, options, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(Math.Max(1, JobOptions.RetentionIntervalHours));
    protected override TimeSpan InitialDelay => TimeSpan.FromMinutes(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var deletedLocations = await services.GetRequiredService<ILocationService>().PurgeExpiredHistoryAsync(ct);

        var db = services.GetRequiredService<AppDbContext>();
        var threshold = clock.GetUtcNow().UtcDateTime.AddDays(-7);
        var deletedTokens = await db.Set<RefreshToken>().Where(t => t.ExpiresAt < threshold).ExecuteDeleteAsync(ct);
        var deletedTracking = await db.Set<JobTrackingToken>().Where(t => t.ExpiresAt < threshold).ExecuteDeleteAsync(ct);

        logger.LogInformation("Veri temizliği: {Locations} konum, {Tokens} refresh token, {Tracking} takip bağlantısı silindi.",
            deletedLocations, deletedTokens, deletedTracking);
    }
}
