using AknaTransport.Business.Abstractions;
using AknaTransport.Business.Options;
using AknaTransport.Domain.Interfaces;
using AknaTransport.Infrastructure.BackgroundJobs;
using AknaTransport.Infrastructure.EArchive;
using AknaTransport.Infrastructure.Email;
using AknaTransport.Infrastructure.Locations;
using AknaTransport.Infrastructure.Pdf;
using AknaTransport.Infrastructure.Persistence;
using AknaTransport.Infrastructure.Persistence.Seed;
using AknaTransport.Infrastructure.Security;
using AknaTransport.Infrastructure.Sms;
using AknaTransport.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AknaTransport.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.SectionName));
        services.AddOptions<LocationTrackingOptions>().Bind(configuration.GetSection(LocationTrackingOptions.SectionName));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddOptions<SmsOptions>().Bind(configuration.GetSection(SmsOptions.SectionName));
        services.AddOptions<EArchiveOptions>().Bind(configuration.GetSection(EArchiveOptions.SectionName));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddOptions<BackgroundJobOptions>().Bind(configuration.GetSection(BackgroundJobOptions.SectionName));
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.Secret.Length >= 32, "Jwt:Secret en az 32 karakter olmalıdır (environment variable: Jwt__Secret).")
            .Validate(o => o.AccessTokenExpirationMinutes is > 0 and <= 1440, "Jwt:AccessTokenExpirationMinutes 1-1440 arasında olmalıdır.")
            .Validate(o => o.RefreshTokenExpirationDays is > 0 and <= 365, "Jwt:RefreshTokenExpirationDays 1-365 arasında olmalıdır.")
            .ValidateOnStart();

        // Veritabanı
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection tanımlı değil (environment variable: ConnectionStrings__DefaultConnection).");
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(3);
            });
        });
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<INumberGenerator, NumberGenerator>();
        services.AddScoped<DatabaseSeeder>();

        // Güvenlik
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        // Entegrasyonlar
        services.AddSingleton<IEmailService, SmtpEmailService>();
        services.AddSingleton<ISmsService, SmsService>();
        services.AddScoped<IEArchiveInvoiceService, EArchiveInvoiceService>();
        services.AddSingleton<IJobReportPdfGenerator, QuestPdfJobReportGenerator>();
        services.AddSingleton<IDraftInvoicePdfGenerator, QuestPdfDraftInvoiceGenerator>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<ILocationBroadcaster, NullLocationBroadcaster>();

        // Arka plan işleri (DatabaseInitializer ilk sırada: migration/seed işleyicilerden önce tamamlanır)
        services.AddHostedService<DatabaseInitializer>();
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<EArchiveStatusSyncWorker>();
        services.AddHostedService<DataRetentionWorker>();

        return services;
    }

    /// <summary>e-Arşiv sağlayıcı adaptörü kaydeder (typed HttpClient ile).</summary>
    public static IServiceCollection AddEArchiveProvider<TAdapter>(this IServiceCollection services)
        where TAdapter : class, IEArchiveProviderAdapter
    {
        services.AddHttpClient<TAdapter>();
        services.AddScoped<IEArchiveProviderAdapter>(sp => sp.GetRequiredService<TAdapter>());
        return services;
    }

    /// <summary>SMS sağlayıcı adaptörü kaydeder.</summary>
    public static IServiceCollection AddSmsProvider<TAdapter>(this IServiceCollection services)
        where TAdapter : class, ISmsProviderAdapter
    {
        services.AddHttpClient<TAdapter>();
        services.AddSingleton<ISmsProviderAdapter>(sp => sp.GetRequiredService<TAdapter>());
        return services;
    }
}
