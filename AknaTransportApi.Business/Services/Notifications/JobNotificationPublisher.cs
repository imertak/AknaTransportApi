using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Services.Notifications;

/// <summary>
/// İş olaylarından bildirim üretir. Bildirim kayıtları ve push outbox mesajları mevcut unit of work'e eklenir;
/// iş değişikliğiyle aynı SaveChanges içinde kalıcı hale gelir. İşlemi yapan kişiye bildirim gönderilmez.
/// </summary>
public sealed class JobNotificationPublisher(
    IRepository<Notification> notifications,
    IRepository<OutboxMessage> outbox,
    IRepository<User> users,
    TimeProvider clock)
{
    /// <summary>
    /// İşin önceki ve yeni haline bakarak gerekli bildirimleri ekler:
    /// yeni şoföre "iş atandı", eski şoföre "iş sizden alındı", şoföre "iptal edildi",
    /// şoför işi ilerlettiğinde yöneticilere durum bildirimi.
    /// </summary>
    public async Task OnJobChangedAsync(TransportJob job, Guid? previousDriverId, JobStatus? previousStatus, Guid actorId, CancellationToken ct)
    {
        var driverChanged = job.AssignedDriverId != previousDriverId;
        var statusChanged = job.Status != previousStatus;

        if (job.Status == JobStatus.Cancelled && statusChanged)
        {
            var reason = string.IsNullOrWhiteSpace(job.CancellationReason) ? "" : $" Sebep: {job.CancellationReason}";
            Add(previousDriverId, actorId, NotificationType.JobCancelled, "İş iptal edildi", $"{Describe(job)} iptal edildi.{reason}", job);
            return;
        }

        if (driverChanged)
        {
            Add(job.AssignedDriverId, actorId, NotificationType.JobAssigned, "Yeni iş atandı",
                $"{Describe(job)} size atandı. {Route(job)}", job);
            Add(previousDriverId, actorId, NotificationType.JobUnassigned, "İş sizden alındı",
                $"{Describe(job)} artık size atanmış değil.", job);
            return;
        }

        if (!statusChanged) return;
        (NotificationType Type, string Title, string Verb)? progress = job.Status switch
        {
            JobStatus.Accepted => (NotificationType.JobAccepted, "İş kabul edildi", "işini kabul etti"),
            JobStatus.OnTheWay => (NotificationType.JobStarted, "Şoför yola çıktı", "işi için yola çıktı"),
            JobStatus.Loading => (NotificationType.JobLoading, "Yükleme başladı", "işinde yüklemeye başladı"),
            JobStatus.Delivering => (NotificationType.JobDelivering, "Teslimata çıkıldı", "işinde teslimata çıktı"),
            JobStatus.Completed => (NotificationType.JobCompleted, "İş tamamlandı", "işini tamamladı"),
            _ => null,
        };
        if (progress is not { } p) return;

        var actorName = await users.QueryAsNoTracking().Where(u => u.Id == actorId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Şoför";
        var admins = await users.QueryAsNoTracking().Where(u => u.Role == UserRole.Admin && u.IsActive && u.Id != actorId)
            .Select(u => u.Id).ToListAsync(ct);
        foreach (var adminId in admins)
            Add(adminId, actorId, p.Type, p.Title, $"{actorName}, {Describe(job)} {p.Verb}.", job);
    }

    /// <summary>Atanmış işin bilgileri (adres, tarih, fiyat vb.) değiştiğinde şoföre haber verir.</summary>
    public void OnJobUpdated(TransportJob job, Guid actorId)
    {
        if (job.IsClosed) return;
        Add(job.AssignedDriverId, actorId, NotificationType.JobUpdated, "İş bilgileri güncellendi",
            $"{Describe(job)} işinin bilgileri değişti. {Route(job)}", job);
    }

    /// <summary>Atanmış ve iptal edilmemiş iş silindiğinde şoföre haber verir (bildirim işe bağlanmaz).</summary>
    public void OnJobDeleted(TransportJob job, Guid actorId)
    {
        if (job.Status is JobStatus.Cancelled or JobStatus.Completed) return;
        Add(job.AssignedDriverId, actorId, NotificationType.JobCancelled, "İş kaldırıldı", $"{Describe(job)} silindi.", job, linkToJob: false);
    }

    private void Add(Guid? userId, Guid actorId, NotificationType type, string title, string body, TransportJob job, bool linkToJob = true)
    {
        if (userId is not { } recipient || recipient == actorId) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var notification = new Notification
        {
            UserId = recipient,
            Type = type,
            Title = Truncate(title, Notification.TitleMaxLength),
            Body = Truncate(body.Trim(), Notification.BodyMaxLength),
            JobId = linkToJob ? job.Id : null,
            CreatedAt = now,
        };
        notifications.Add(notification);
        OutboxWriter.Enqueue(outbox, OutboxMessageTypes.PushNotification, new PushNotificationPayload(notification.Id), now);
    }

    private static string Describe(TransportJob job)
    {
        var date = job.JobDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(job.StoreName) ? $"{job.JobNumber} ({date})" : $"{job.JobNumber} {job.StoreName} ({date})";
    }

    private static string Route(TransportJob job) =>
        string.IsNullOrWhiteSpace(job.PickupAddress) ? "" : $"{job.PickupAddress} → {job.DeliveryAddress}";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
