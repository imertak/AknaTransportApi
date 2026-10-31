using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransportApi.Infrastructure.Configurations;

public sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("vehicles");
        b.HasKey(x => x.Id);
        b.Property(x => x.PlateNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.Brand).HasMaxLength(100);
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.VehicleType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(500);
        b.HasIndex(x => x.PlateNumber).IsUnique();
        b.HasIndex(x => x.IsActive);
        b.HasMany(x => x.DriverAssignments).WithOne(a => a.Vehicle).HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DriverVehicleConfiguration : IEntityTypeConfiguration<DriverVehicle>
{
    public void Configure(EntityTypeBuilder<DriverVehicle> b)
    {
        b.ToTable("driver_vehicles");
        b.HasKey(x => x.Id);
        // Bir araç aynı anda yalnızca bir şoföre aktif olarak atanabilir.
        b.HasIndex(x => x.VehicleId).IsUnique().HasFilter("\"IsActive\"").HasDatabaseName("IX_driver_vehicles_VehicleId_active");
        b.HasIndex(x => new { x.DriverId, x.IsActive });
        b.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
