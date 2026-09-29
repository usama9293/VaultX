using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PasswordManager.Domain.Entities;

namespace PasswordManager.Infrastructure.Persistence.Configurations;

public class VaultConfiguration : IEntityTypeConfiguration<Vault>
{
    public void Configure(EntityTypeBuilder<Vault> builder)
    {
        builder.ToTable("Vaults");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.Property(x => x.EncryptedKey)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.KeyNonce)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.KeyAuthenticationTag)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasOne(x => x.User)
            .WithOne(x => x.Vault)
            .HasForeignKey<Vault>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Entries)
            .WithOne(x => x.Vault)
            .HasForeignKey(x => x.VaultId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
