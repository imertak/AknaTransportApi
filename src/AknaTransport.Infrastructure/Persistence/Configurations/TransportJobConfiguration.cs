using AknaTransport.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransport.Infrastructure.Persistence.Configurations;

public sealed class TransportJobConfiguration : IEntityTypeConfiguration<TransportJob>
{
    public void Configure(EntityTypeBuilder<TransportJob> b)
    {
        b.ToTable("transport_jobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.JobNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.StoreName).HasMaxLength(200).IsRequired();
        b.Property(x => x.VehicleType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PickupAddress).HasMaxLength(500).IsRequired();
        b.Property(x => x.DeliveryAddress).HasMaxLength(500).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Price).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Ignore(x => x.IsClosed);
        b.Ignore(x => x.IsTrackable);

        b.HasIndex(x => x.JobNumber).IsUnique();
        b.HasIndex(x => x.JobDate);
        b.HasIndex(x => new { x.Status, x.JobDate });
        b.HasIndex(x => new { x.AssignedDriverId, x.Status });
        b.HasIndex(x => new { x.CustomerId, x.JobDate });
        b.HasIndex(x => x.InvoiceId);

        b.HasOne(x => x.Customer).WithMany(c => c.Jobs).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AssignedDriver).WithMany().HasForeignKey(x => x.AssignedDriverId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.StatusHistory).WithOne(h => h.Job).HasForeignKey(h => h.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class JobStatusHistoryConfiguration : IEntityTypeConfiguration<JobStatusHistory>
{
    public void Configure(EntityTypeBuilder<JobStatusHistory> b)
    {
        b.ToTable("job_status_histories");
        b.HasKey(x => x.Id);
        b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.JobId, x.ChangedAt });
        b.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
