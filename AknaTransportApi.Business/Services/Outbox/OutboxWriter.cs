using System.Text.Json;

namespace AknaTransportApi.Business.Services.Outbox;

public static class OutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Outbox kaydını mevcut unit of work'e ekler. Kayıt, iş verisiyle aynı SaveChanges /
    /// transaction içinde kalıcı hale gelir; böylece "fatura kesildi ama mail kuyruğa alınmadı"
    /// durumu oluşmaz.
    /// </summary>
    public static OutboxMessage Enqueue<TPayload>(IRepository<OutboxMessage> outbox, string type, TPayload payload, DateTime now)
    {
        var message = new OutboxMessage
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = OutboxMessageStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
        };
        outbox.Add(message);
        return message;
    }

    public static T Deserialize<T>(string payload) =>
        JsonSerializer.Deserialize<T>(payload, JsonOptions) ?? throw new InvalidOperationException("Outbox içeriği okunamadı.");
}
