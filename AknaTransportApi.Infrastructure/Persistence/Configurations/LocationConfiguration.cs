using AknaTransport.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransport.Infrastructure.Persistence.Configurations;

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations");
        b.HasKey(x => x.Id);
        // Geçmiş sorguları (iş + zaman aralığı) ve saklama politikası silmeleri için.
        b.HasIndex(x => new { x.JobId, x.RecordedAt });
        b.HasIndex(x => x.RecordedAt);
        b.HasIndex(x => x.DriverId);
        b.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobLatestLocationConfiguration : IEntityTypeConfiguration<JobLatestLocation>
{
    public void Configure(EntityTypeBuilder<JobLatestLocation> b)
    {
        b.ToTable("job_latest_locations");
        // İş başına tek satır: son konum sorgusu birincil anahtar araması ile yapılır.
        b.HasKey(x => x.JobId);
        b.HasIndex(x => x.DriverId);
        b.HasOne(x => x.Job).WithOne().HasForeignKey<JobLatestLocation>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class JobTrackingTokenConfiguration : IEntityTypeConfiguration<JobTrackingToken>
{
    public void Configure(EntityTypeBuilder<JobTrackingToken> b)
    {
        b.ToTable("job_tracking_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.JobId);
        b.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
    }
}
