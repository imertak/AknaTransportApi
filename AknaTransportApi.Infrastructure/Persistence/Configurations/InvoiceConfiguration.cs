using AknaTransport.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransport.Infrastructure.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");
        b.HasKey(x => x.Id);
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SubTotal).HasPrecision(18, 2);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Property(x => x.GrandTotal).HasPrecision(18, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.EArchiveReference).HasMaxLength(100);
        b.Property(x => x.EArchiveInvoiceNumber).HasMaxLength(50);
        b.Property(x => x.EArchiveInvoiceUrl).HasMaxLength(1000);
        b.Property(x => x.EArchiveProvider).HasMaxLength(50);
        b.Property(x => x.InvoicePdfPath).HasMaxLength(300);
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Ignore(x => x.IsEditable);

        b.HasIndex(x => x.InvoiceNumber).IsUnique();
        b.HasIndex(x => x.EArchiveReference).IsUnique();
        b.HasIndex(x => new { x.CustomerId, x.InvoiceDate });
        b.HasIndex(x => new { x.Status, x.InvoiceDate });

        b.HasOne(x => x.Customer).WithMany(c => c.Invoices).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(i => i.Invoice).HasForeignKey(i => i.InvoiceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> b)
    {
        b.ToTable("invoice_items");
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.VatRate).HasPrecision(5, 2);
        b.Property(x => x.LineTotal).HasPrecision(18, 2);
        // Aynı iş aynı faturaya iki kez eklenemez. Bir işin aynı anda tek faturada olması
        // transport_jobs.InvoiceId üzerinden koşullu güncelleme ile garanti edilir.
        b.HasIndex(x => new { x.InvoiceId, x.JobId }).IsUnique();
        b.HasIndex(x => x.JobId);
        b.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
