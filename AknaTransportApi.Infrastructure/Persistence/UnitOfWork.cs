using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Infrastructure.Persistence;

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
