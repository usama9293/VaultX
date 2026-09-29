using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PasswordManager.Domain.Entities;

namespace PasswordManager.Infrastructure.Persistence.Configurations;

public class VaultEntryConfiguration : IEntityTypeConfiguration<VaultEntry>
{
    public void Configure(EntityTypeBuilder<VaultEntry> builder)
    {
        builder.ToTable("VaultEntries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.VaultId)
            .IsRequired();

        builder.HasIndex(x => x.VaultId);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.WebsiteUrl)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(x => x.Username)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.EncryptedPassword)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.PasswordNonce)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.PasswordAuthenticationTag)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(x => x.Notes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();
    }
}
