using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransportApi.Infrastructure.Configurations;

public sealed class TransportOfferConfiguration : IEntityTypeConfiguration<TransportOffer>
{
    public void Configure(EntityTypeBuilder<TransportOffer> b)
    {
        b.ToTable("transport_offers");
        b.HasKey(x => x.Id);
        b.Property(x => x.OfferNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(150);
        b.Property(x => x.CustomerPhone).HasMaxLength(30);
        b.Property(x => x.CustomerEmail).HasMaxLength(200);
        b.Property(x => x.CustomerAddress).HasMaxLength(TransportOffer.AddressMaxLength);
        b.Property(x => x.LoadingPoint).HasMaxLength(TransportOffer.PointMaxLength).IsRequired();
        b.Property(x => x.LoadingAddress).HasMaxLength(TransportOffer.AddressMaxLength);
        b.Property(x => x.UnloadingPoint).HasMaxLength(TransportOffer.PointMaxLength).IsRequired();
        b.Property(x => x.UnloadingAddress).HasMaxLength(TransportOffer.AddressMaxLength);
        b.Property(x => x.TransportType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.VehicleType).HasMaxLength(50).IsRequired();
        b.Property(x => x.CargoDescription).HasMaxLength(1000);
        b.Property(x => x.DistanceKm).HasPrecision(10, 1);
        b.Property(x => x.SubTotal).HasPrecision(18, 2);
        b.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.PaymentTerms).HasMaxLength(TransportOffer.TextMaxLength);
        b.Property(x => x.DeliveryTerms).HasMaxLength(TransportOffer.TextMaxLength);
        b.Property(x => x.CustomerNotes).HasMaxLength(TransportOffer.TextMaxLength);
        b.Property(x => x.InternalNotes).HasMaxLength(TransportOffer.TextMaxLength);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.IsDeleted).HasDefaultValue(false);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Ignore(x => x.IsConverted);
        b.Ignore(x => x.NetAmount);

        // Silinen teklifler standart sorgulardan çıkar.
        b.HasQueryFilter(x => !x.IsDeleted);

        b.HasIndex(x => x.OfferNumber).IsUnique();
        // Bir işin kaynağı tek bir teklif olabilir; aynı tekliften ikinci iş ise concurrency stamp ile engellenir.
        b.HasIndex(x => x.ConvertedJobId).IsUnique();
        b.HasIndex(x => new { x.Status, x.ValidUntil });
        b.HasIndex(x => new { x.CustomerId, x.OfferDate });
        b.HasIndex(x => x.OfferDate);

        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConvertedJob).WithMany().HasForeignKey(x => x.ConvertedJobId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TransportOffer>().WithMany().HasForeignKey(x => x.CopiedFromOfferId).OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Items).WithOne(i => i.TransportOffer).HasForeignKey(i => i.TransportOfferId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.History).WithOne(h => h.TransportOffer).HasForeignKey(h => h.TransportOfferId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TransportOfferItemConfiguration : IEntityTypeConfiguration<TransportOfferItem>
{
    public void Configure(EntityTypeBuilder<TransportOfferItem> b)
    {
        b.ToTable("transport_offer_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(TransportOfferItem.DescriptionMaxLength).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(TransportOfferItem.UnitMaxLength);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.TotalPrice).HasPrecision(18, 2);
        b.HasIndex(x => new { x.TransportOfferId, x.Sequence }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_transport_offer_items_quantity", "\"Quantity\" > 0"));
    }
}

public sealed class TransportOfferHistoryConfiguration : IEntityTypeConfiguration<TransportOfferHistory>
{
    public void Configure(EntityTypeBuilder<TransportOfferHistory> b)
    {
        b.ToTable("transport_offer_histories");
        b.HasKey(x => x.Id);
        b.Property(x => x.OldStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.TransportOfferId, x.CreatedAt });
        b.HasOne(x => x.ChangedByUser).WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
