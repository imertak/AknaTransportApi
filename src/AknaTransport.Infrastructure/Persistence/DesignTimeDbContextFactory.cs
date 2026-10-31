using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AknaTransport.Infrastructure.Persistence;

/// <summary>
/// dotnet ef komutları için. Bağlantı dizesi AKNA_DESIGN_CONNECTION environment variable'ından
/// okunur; migration oluşturmak için veritabanına bağlanmak gerekmez.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("AKNA_DESIGN_CONNECTION")
                         ?? "Host=localhost;Port=5432;Database=akna_transport;Username=akna";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection, o => o.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options, TimeProvider.System);
    }
}
