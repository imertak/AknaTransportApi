using AknaTransport.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransport.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactPerson).HasMaxLength(150);
        b.Property(x => x.PhoneNumber).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.TaxOffice).HasMaxLength(100);
        b.Property(x => x.TaxNumber).HasMaxLength(11);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => x.CompanyName);
        b.HasIndex(x => x.IsActive);
        b.HasIndex(x => x.TaxNumber);
    }
}
