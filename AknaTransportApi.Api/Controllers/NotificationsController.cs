using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AknaTransportApi.Api.Controllers;

/// <summary>
/// Giriş yapmış kullanıcının bildirimleri ("Yeni iş atandı", "İş tamamlandı" vb.) ve telefon bildirimi için cihaz kaydı.
/// Şoför kendisine atanan/alınan/iptal edilen işler için, yönetici şoförün iş durumunu ilerletmesi için bildirim alır.
/// </summary>
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    /// <summary>Bildirimler, en yenisi önce. unreadOnly=true yalnızca okunmamışları getirir.</summary>
    [HttpGet]
    public async Task<ActionResult<BaseResponse<PagedResult<NotificationDto>>>> List([FromQuery] NotificationQuery query, CancellationToken ct) =>
        Success(await notifications.ListMineAsync(query, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<BaseResponse<UnreadCountDto>>> UnreadCount(CancellationToken ct) =>
        Success(await notifications.GetUnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<BaseResponse>> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return Done("Bildirim okundu olarak işaretlendi.");
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<BaseResponse>> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return Done("Tüm bildirimler okundu olarak işaretlendi.");
    }

    /// <summary>Telefonun Expo push token'ını kaydeder. Uygulama her açılışta ve girişte çağırır.</summary>
    [HttpPost("devices")]
    [AllowWhenPasswordChangeRequired]
    public async Task<ActionResult<BaseResponse>> RegisterDevice(RegisterPushDeviceRequest request, CancellationToken ct)
    {
        await notifications.RegisterDeviceAsync(request, ct);
        return Done("Cihaz bildirimler için kaydedildi.");
    }

    /// <summary>Çıkış yaparken telefonun token'ını siler; bu telefona artık bildirim gitmez.</summary>
    [HttpPost("devices/remove")]
    [AllowWhenPasswordChangeRequired]
    public async Task<ActionResult<BaseResponse>> UnregisterDevice(UnregisterPushDeviceRequest request, CancellationToken ct)
    {
        await notifications.UnregisterDeviceAsync(request, ct);
        return Done("Cihaz kaydı silindi.");
    }
}
