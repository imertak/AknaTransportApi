using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Infrastructure.Persistence.Repositories;

public sealed class Repository<T>(AppDbContext context) : IRepository<T> where T : class
{
    private readonly DbSet<T> _set = context.Set<T>();

    public IQueryable<T> Query() => _set;

    public IQueryable<T> QueryAsNoTracking() => _set.AsNoTracking();

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _set.FindAsync([id], cancellationToken);

    public void Add(T entity) => _set.Add(entity);

    public void AddRange(IEnumerable<T> entities) => _set.AddRange(entities);

    public void Remove(T entity) => _set.Remove(entity);
}
