using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AknaTransportApi.Business.Interfaces.Services;

public interface IOutboxProcessor
{
    /// <summary>Zamanı gelmiş outbox mesajlarını işler; işlenen mesaj sayısını döndürür.</summary>
    Task<int> ProcessPendingAsync(CancellationToken ct);
}
