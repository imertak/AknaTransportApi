namespace AknaTransportApi.Domain.Interfaces;

/// <summary>Değişiklikleri tek seferde kaydeder ve gerektiğinde işlemleri tek transaction içinde yürütür.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Verilen işlemi tek bir veritabanı transaction'ı içinde çalıştırır.</summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
}
