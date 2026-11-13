namespace AknaTransportApi.Business.Interfaces.Services;

public interface ITransportOfferService
{
    Task<PagedResult<TransportOfferListItemDto>> ListAsync(TransportOfferQuery query, CancellationToken ct);
    /// <summary>Arama, müşteri ve tarih filtresine uyan tekliflerin durum dağılımı (durum filtresi uygulanmaz).</summary>
    Task<TransportOfferStatsDto> StatsAsync(TransportOfferQuery query, CancellationToken ct);
    Task<TransportOfferDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<TransportOfferDetailDto> CreateAsync(TransportOfferRequest request, CancellationToken ct);
    Task<TransportOfferDetailDto> UpdateAsync(Guid id, TransportOfferRequest request, CancellationToken ct);
    /// <summary>Teklifi yeni numarayla taslak olarak kopyalar (bugünün tarihi, 7 gün geçerlilik).</summary>
    Task<TransportOfferDetailDto> CopyAsync(Guid id, CancellationToken ct);
    Task<TransportOfferDetailDto> MarkSentAsync(Guid id, CancellationToken ct);
    Task<TransportOfferDetailDto> AcceptAsync(Guid id, OfferStatusRequest request, CancellationToken ct);
    Task<TransportOfferDetailDto> RejectAsync(Guid id, OfferStatusRequest request, CancellationToken ct);
    Task<TransportOfferDetailDto> CancelAsync(Guid id, OfferStatusRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<FileDownload> GetPdfAsync(Guid id, CancellationToken ct);
    /// <summary>Kabul edilmiş tekliften mevcut iş oluşturma servisiyle nakliye işi oluşturur (tek transaction).</summary>
    Task<ConvertOfferResultDto> ConvertToJobAsync(Guid id, CancellationToken ct);
}
