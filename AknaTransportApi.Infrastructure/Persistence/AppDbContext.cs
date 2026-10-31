using Microsoft.EntityFrameworkCore;

namespace AknaTransportApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider clock) : DbContext(options)
{
    public const string JobNumberSequence = "job_number_seq";
    public const string InvoiceNumberSequence = "invoice_number_seq";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<TransportJob> TransportJobs => Set<TransportJob>();
    public DbSet<JobStatusHistory> JobStatusHistories => Set<JobStatusHistory>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<JobLatestLocation> JobLatestLocations => Set<JobLatestLocation>();
    public DbSet<JobTrackingToken> JobTrackingTokens => Set<JobTrackingToken>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Kimlikler uygulama tarafında (Guid v7) üretilir. Böylece navigation koleksiyonuna eklenen
        // yeni kayıtlar (ör. durum geçmişi) EF tarafından doğru şekilde "Added" olarak algılanır.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        if (Database.IsNpgsql())
        {
            modelBuilder.HasSequence<long>(JobNumberSequence).StartsAt(1).IncrementsBy(1);
            modelBuilder.HasSequence<long>(InvoiceNumberSequence).StartsAt(1).IncrementsBy(1);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Para alanları için varsayılan hassasiyet; float/double kullanılmaz.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    private void ApplyTimestamps()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
