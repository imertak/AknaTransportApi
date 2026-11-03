using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransportApi.Infrastructure.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Title).HasMaxLength(Notification.TitleMaxLength).IsRequired();
        b.Property(x => x.Body).HasMaxLength(Notification.BodyMaxLength).IsRequired();
        b.Ignore(x => x.IsRead);
        // Kullanıcının bildirim listesi ve okunmamış sayısı için.
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
        b.HasIndex(x => new { x.UserId, x.ReadAt });
        b.HasIndex(x => x.JobId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> b)
    {
        b.ToTable("push_devices");
        b.HasKey(x => x.Id);
        b.Property(x => x.Token).HasMaxLength(PushDevice.TokenMaxLength).IsRequired();
        b.Property(x => x.Platform).HasMaxLength(20);
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
