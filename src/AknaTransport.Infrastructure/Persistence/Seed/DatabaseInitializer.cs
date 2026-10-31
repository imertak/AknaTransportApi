using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AknaTransport.Infrastructure.Persistence.Seed;

/// <summary>Uygulama açılırken migration ve seed işlemlerini, HTTP istekleri kabul edilmeden önce çalıştırır.</summary>
public sealed class DatabaseInitializer(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().RunAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
