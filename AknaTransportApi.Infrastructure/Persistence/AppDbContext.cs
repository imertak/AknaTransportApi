using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AknaTransportApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider clock) : DbContext(options)
{
    public const string JobNumberSequence = "job_number_seq";
    public const string InvoiceNumberSequence = "invoice_number_seq";
    public const string OfferNumberSequence = "offer_number_seq";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<TransportJob> TransportJobs => Set<TransportJob>();
    public DbSet<JobStatusHistory> JobStatusHistories => Set<JobStatusHistory>();
    public DbSet<TransportJobStop> TransportJobStops => Set<TransportJobStop>();
    public DbSet<TransportJobContact> TransportJobContacts => Set<TransportJobContact>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<DriverVehicle> DriverVehicles => Set<DriverVehicle>();
    public DbSet<VehicleTypeDefinition> VehicleTypes => Set<VehicleTypeDefinition>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<JobLatestLocation> JobLatestLocations => Set<JobLatestLocation>();
    public DbSet<JobTrackingToken> JobTrackingTokens => Set<JobTrackingToken>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushDevice> PushDevices => Set<PushDevice>();
    public DbSet<CustomerTransaction> CustomerTransactions => Set<CustomerTransaction>();
    public DbSet<TransportOffer> TransportOffers => Set<TransportOffer>();
    public DbSet<TransportOfferItem> TransportOfferItems => Set<TransportOfferItem>();
    public DbSet<TransportOfferHistory> TransportOfferHistories => Set<TransportOfferHistory>();

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
            modelBuilder.HasSequence<long>(OfferNumberSequence).StartsAt(1).IncrementsBy(1);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Para alanları için varsayılan hassasiyet; float/double kullanılmaz.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        var jobChanges = CollectJobChanges();
        var result = await base.SaveChangesAsync(cancellationToken);
        if (jobChanges.Count > 0) await PublishJobChangesAsync(jobChanges);
        return result;
    }

    /// <summary>Canlı haritayı ilgilendiren iş alanları. Soft delete alanları varsa onlar da izlenir.</summary>
    private static readonly HashSet<string> RealtimeJobProperties =
        [nameof(TransportJob.Status), nameof(TransportJob.AssignedDriverId), nameof(TransportJob.AssignedVehicleId), "IsDeleted", "DeletedAt"];

    /// <summary>
    /// Kaydedilmeden önce durum/şoför/araç değişen veya silinen işleri toplar. Yayın, kayıt başarılı olduktan
    /// sonra yapılır; böylece iş servislerine dokunmadan tüm iş akışları (iptal, silme, atama) kapsanır.
    /// </summary>
    private List<JobChangedDto> CollectJobChanges()
    {
        var list = new List<JobChangedDto>();
        foreach (var entry in ChangeTracker.Entries<TransportJob>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.State == EntityState.Modified && !entry.Properties.Any(p => p.IsModified && RealtimeJobProperties.Contains(p.Metadata.Name))) continue;

            var driverProperty = entry.Property(j => j.AssignedDriverId);
            var drivers = new[] { driverProperty.CurrentValue, entry.State == EntityState.Added ? null : driverProperty.OriginalValue }
                .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
            var removed = entry.State == EntityState.Deleted || IsSoftDeleted(entry);
            list.Add(new JobChangedDto(entry.Entity.Id, entry.Entity.Status, drivers, removed));
        }
        return list;
    }

    private static bool IsSoftDeleted(EntityEntry<TransportJob> entry) =>
        entry.Metadata.FindProperty("IsDeleted") is not null && entry.Property("IsDeleted").CurrentValue is true;

    private async Task PublishJobChangesAsync(List<JobChangedDto> changes)
    {
        var services = this.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;
        if (services?.GetService<ILocationBroadcaster>() is not { } broadcaster) return;
        foreach (var change in changes)
        {
            try
            {
                await broadcaster.PublishJobChangedAsync(change, CancellationToken.None);
            }
            catch
            {
                // Gerçek zamanlı ipucu en iyi çaba ile gönderilir; kayıt zaten tamamlandı.
            }
        }
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
