using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Business.Common.Pagination;

public class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Sayfalama ve sıralama parametreleri.</summary>
public class PagedQuery
{
    public const int MaxPageSize = 200;

    private int _pageNumber = 1;
    private int _pageSize = 20;

    public int PageNumber { get => _pageNumber; set => _pageNumber = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; set => _pageSize = value < 1 ? 20 : Math.Min(value, MaxPageSize); }

    /// <summary>Sıralama alanı (endpoint'e göre değişir).</summary>
    public string? SortBy { get; set; }

    /// <summary>asc veya desc.</summary>
    public string? SortDirection { get; set; }

    public bool Descending => !string.Equals(SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagedQuery paging, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((paging.PageNumber - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(ct);
        return new PagedResult<T> { Items = items, PageNumber = paging.PageNumber, PageSize = paging.PageSize, TotalCount = total };
    }
}

/// <summary>Bir ayın liste özeti (iptal edilenler tutara katılmaz, adede katılır).</summary>
public sealed record MonthTotalDto(int Year, int Month, int Count, decimal Total);

/// <summary>
/// Sayfalı listenin filtreye uyan tüm kayıtları için özet: toplam adet, tutar ve ay ay dağılım. Uygulama
/// listeyi sayfa sayfa çekerken başlıklardaki adet ve tutarları buradan gösterir.
/// </summary>
public sealed record ListSummaryDto(int Count, decimal Total, IReadOnlyList<MonthTotalDto> Months)
{
    public static ListSummaryDto FromDays(IEnumerable<(DateOnly Date, int Count, decimal Total)> days)
    {
        var months = days
            .GroupBy(d => (d.Date.Year, d.Date.Month))
            .Select(g => new MonthTotalDto(g.Key.Year, g.Key.Month, g.Sum(x => x.Count), g.Sum(x => x.Total)))
            .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
            .ToList();
        return new ListSummaryDto(months.Sum(m => m.Count), months.Sum(m => m.Total), months);
    }
}
