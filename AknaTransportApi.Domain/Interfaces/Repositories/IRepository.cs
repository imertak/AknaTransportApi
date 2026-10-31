
namespace AknaTransportApi.Domain.Interfaces.Repositories;

/// <summary>
/// Genel repository sözleşmesi. Sorgular IQueryable üzerinden projeksiyonla yazılır;
/// böylece yalnızca ihtiyaç duyulan kolonlar veritabanından çekilir.
/// </summary>
public interface IRepository<T> where T : class
{
    /// <summary>Değişiklik takibi yapılan sorgu (güncelleme senaryoları).</summary>
    IQueryable<T> Query();

    /// <summary>Salt okunur sorgu (AsNoTracking).</summary>
    IQueryable<T> QueryAsNoTracking();

    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(T entity);

    void AddRange(IEnumerable<T> entities);

    void Remove(T entity);
}
