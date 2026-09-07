using HBA.Deliveries.Domain.Partners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("partners");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PartnerId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DailyQuota).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.CreatedAtUtc).IsRequired();

        builder.Property(p => p.WebhookUrl).HasMaxLength(500);

        // LE SECRET DE WEBHOOK EST STOCKÉ EN CLAIR — ET IL LE DOIT.
        builder.Property(p => p.WebhookSecret).HasMaxLength(200);

        builder.OwnsMany(p => p.ApiKeys, key =>
        {
            key.ToTable("partner_api_keys");
            key.WithOwner().HasForeignKey("partner_id");
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).ValueGeneratedNever();

            key.Property(k => k.Prefix).HasMaxLength(24).IsRequired();
            key.Property(k => k.Hash).HasMaxLength(64).IsRequired();
            key.Property(k => k.Label).HasMaxLength(120);
            key.Property(k => k.CreatedAtUtc).IsRequired();
            key.Property(k => k.RevokedAtUtc);
            key.Property(k => k.LastUsedAtUtc);

            // L'INDEX QUI REND L'AUTHENTIFICATION VIABLE.
            key.HasIndex(k => k.Prefix)
                .IsUnique()
                .HasDatabaseName("ux_partner_api_keys_prefix");
        });

        builder.HasIndex(p => p.Status).HasDatabaseName("ix_partners_status");
    }
}
