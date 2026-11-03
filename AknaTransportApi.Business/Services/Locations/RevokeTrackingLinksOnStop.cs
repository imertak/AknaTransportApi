using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Locations;

/// <summary>İş iptal edilince veya silinince müşteriye verilen canlı takip bağlantılarını kapatır.</summary>
public sealed class RevokeTrackingLinksOnStop(
    IRepository<JobTrackingToken> trackingTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IJobTrackingStopHandler
{
    public async Task OnTrackingStoppedAsync(Guid jobId, JobTrackingStopReason reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Silinen işin bağlantıları da kapatılır; global filtre token sorgusunu etkilemez (token doğrudan sorgulanır).
        var tokens = await trackingTokens.Query().IgnoreQueryFilters()
            .Where(t => t.JobId == jobId && t.RevokedAt == null).ToListAsync(ct);
        if (tokens.Count == 0) return;
        foreach (var t in tokens) t.RevokedAt = now;
        await unitOfWork.SaveChangesAsync(ct);
    }
}
