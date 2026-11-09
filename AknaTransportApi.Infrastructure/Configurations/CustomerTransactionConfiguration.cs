using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransportApi.Infrastructure.Configurations;

public sealed class CustomerTransactionConfiguration : IEntityTypeConfiguration<CustomerTransaction>
{
    public void Configure(EntityTypeBuilder<CustomerTransaction> b)
    {
        b.ToTable("customer_transactions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.Method).HasMaxLength(CustomerTransaction.MethodMaxLength);
        b.Property(x => x.Description).HasMaxLength(CustomerTransaction.DescriptionMaxLength);
        b.Ignore(x => x.SignedAmount);
        b.HasIndex(x => new { x.CustomerId, x.TransactionDate });
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
