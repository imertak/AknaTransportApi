using AknaTransport.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AknaTransport.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Email).HasMaxLength(200).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(200).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PasswordResetTokenHash).HasMaxLength(128);
        b.Ignore(x => x.IsAdmin);
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.HasIndex(x => new { x.Role, x.IsActive });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.CreatedByIp).HasMaxLength(64);
        b.Property(x => x.RevokedReason).HasMaxLength(200);
        // Eşzamanlı iki yenileme isteğinden yalnızca biri başarılı olur.
        b.Property(x => x.RevokedAt).IsConcurrencyToken();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.HasIndex(x => x.ExpiresAt);
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
