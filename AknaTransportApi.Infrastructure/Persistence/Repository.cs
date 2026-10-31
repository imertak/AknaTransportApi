using AknaTransport.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AknaTransport.Infrastructure.Persistence;

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

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        // Zaten bir transaction varsa ona katılır.
        if (context.Database.CurrentTransaction is not null) return await operation(cancellationToken);

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                // Başarısız transaction sonrası bağlamda kalan değişiklikler temizlenir.
                context.ChangeTracker.Clear();
                throw;
            }
        }, cancellationToken);
    }
}
