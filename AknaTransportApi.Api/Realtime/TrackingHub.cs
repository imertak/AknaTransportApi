using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AknaTransportApi.Api.Realtime;

/// <summary>
/// Canlı araç takibi hub'ı (<c>/hubs/tracking</c>). Bağlantı JWT ile doğrulanır (WebSocket'te token
/// <c>access_token</c> sorgu parametresiyle gelir). Gruplar:
/// <list type="bullet">
/// <item><c>admins</c>: yöneticiler; tüm aktif işlerin konumlarını ve iş değişikliklerini alır.</item>
/// <item><c>user-{id}</c>: kullanıcının kendisi; kendisine atanan/alınan işlerin değişiklik ipuçlarını alır.</item>
/// <item><c>job-{id}</c>: <see cref="WatchJob"/> ile katılınır; sunucu işin görüntülenme yetkisini doğrular.</item>
/// </list>
/// İstemciye giden olaylar: <c>locationUpdated</c> (<see cref="LocationDto"/>) ve <c>jobChanged</c> (<see cref="JobChangedDto"/>).
/// </summary>
[Authorize]
public sealed class TrackingHub(ILocationService locationService) : Hub
{
    public const string Path = "/hubs/tracking";
    public const string AdminsGroup = "admins";
    public const string LocationUpdatedEvent = "locationUpdated";
    public const string JobChangedEvent = "jobChanged";

    public static string UserGroup(Guid userId) => $"user-{userId:N}";
    public static string JobGroup(Guid jobId) => $"job-{jobId:N}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        // Geçici şifreyle giriş yapan kullanıcı, REST uç noktalarında olduğu gibi canlı takibe de erişemez.
        if (UserId(user) is not { } userId || user?.FindFirst(AppClaimTypes.MustChangePassword)?.Value == "true")
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);
        if (IsAdmin(user)) await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    /// <summary>Tek bir işin canlı konumunu izler. Yönetici her işi, şoför yalnızca kendisine atanmış işi izleyebilir.</summary>
    public async Task WatchJob(Guid jobId)
    {
        var userId = UserId(Context.User) ?? throw new HubException("Oturum bulunamadı.");
        if (!await locationService.CanViewJobAsync(jobId, userId, IsAdmin(Context.User), Context.ConnectionAborted))
            throw new HubException("Bu işin konumunu izleme yetkiniz yok.");
        await Groups.AddToGroupAsync(Context.ConnectionId, JobGroup(jobId), Context.ConnectionAborted);
    }

    public Task UnwatchJob(Guid jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, JobGroup(jobId), Context.ConnectionAborted);

    private static Guid? UserId(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub"), out var id) && id != Guid.Empty ? id : null;

    private static bool IsAdmin(ClaimsPrincipal? user) => user?.FindFirstValue(ClaimTypes.Role) == nameof(UserRole.Admin);
}
