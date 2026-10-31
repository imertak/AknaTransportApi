using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AknaTransportApi.Business.Tests.Infrastructure;

/// <summary>
/// API'yi geçici bir SQLite veritabanıyla ayağa kaldırır. Arka plan işleyicileri kapalıdır;
/// outbox testlerde doğrudan IOutboxProcessor ile işlenir.
/// </summary>
public class AknaApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "AdminTest123!";

    // Her factory için ayrı geçici SQLite dosyası; her DbContext kendi bağlantısını açar.
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"akna-test-{Guid.NewGuid():N}.db");

    public FakeEmailService Email { get; } = new();
    public FakeEArchiveAdapter EArchive { get; } = new();

    /// <summary>false ise e-Arşiv sağlayıcısı yapılandırılmamış kabul edilir.</summary>
    protected virtual bool EArchiveConfigured => true;

    /// <summary>Kullanılacak e-Arşiv adaptörünün adı.</summary>
    protected virtual string EArchiveProvider => FakeEArchiveAdapter.ProviderName;

    /// <summary>Alt sınıfların eklediği ek ayarlar.</summary>
    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    protected virtual void ConfigureExtraServices(IServiceCollection services) { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>(ExtraSettings)
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=unused",
            ["Jwt:Secret"] = "test-secret-0123456789-abcdefghijklmnopqrstuvwxyz",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Seed:DemoData"] = "false",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["BackgroundJobs:Enabled"] = "false",
            ["EArchive:Provider"] = EArchiveConfigured ? EArchiveProvider : "",
            ["Storage:RootPath"] = Path.Combine(Path.GetTempPath(), "akna-tests", Guid.NewGuid().ToString("N")),
            ["RateLimiting:GlobalPerMinute"] = "100000",
            ["RateLimiting:AuthPerMinute"] = "100000",
            ["RateLimiting:LocationPerMinute"] = "100000",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_databasePath};Default Timeout=30;Pooling=False"));

            // Şema, seed işleminden (DatabaseInitializer) önce oluşturulmalı: ilk hosted service olarak eklenir.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService, EnsureSchemaHostedService>());

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);
            services.AddSingleton<IEArchiveProviderAdapter>(EArchive);
            ConfigureExtraServices(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}

public sealed class NoEArchiveApiFactory : AknaApiFactory
{
    protected override bool EArchiveConfigured => false;
}

/// <summary>Test veritabanı şemasını model üzerinden oluşturur (PostgreSQL migration'ları SQLite'ta çalışmaz).</summary>
internal sealed class EnsureSchemaHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
