using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransportApi.Infrastructure.Configurations;

public sealed class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleTypeDefinition>
{
    /// <summary>İlk kurulumda gelen tipler. Kodları eski enum adlarıyla aynıdır; mevcut iş ve araç kayıtları değişmeden eşleşir.</summary>
    public static readonly (Guid Id, string Code, string Name, int Sort)[] Defaults =
    [
        (new Guid("0193a000-0000-7000-8000-000000000001"), VehicleType.Panelvan, "Panelvan", 10),
        (new Guid("0193a000-0000-7000-8000-000000000002"), VehicleType.SmallPanelvan, "Küçük Panelvan", 20),
        (new Guid("0193a000-0000-7000-8000-000000000003"), VehicleType.Pickup, "Kamyonet", 30),
        (new Guid("0193a000-0000-7000-8000-000000000004"), VehicleType.Truck, "Kamyon", 40),
        (new Guid("0193a000-0000-7000-8000-000000000005"), VehicleType.Other, "Diğer", 90),
    ];

    public void Configure(EntityTypeBuilder<VehicleTypeDefinition> b)
    {
        b.ToTable("vehicle_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasIndex(x => x.Name).IsUnique();
        var seededAt = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        b.HasData(Defaults.Select(d => new VehicleTypeDefinition { Id = d.Id, Code = d.Code, Name = d.Name, SortOrder = d.Sort, IsActive = true, CreatedAt = seededAt }));
    }
}
