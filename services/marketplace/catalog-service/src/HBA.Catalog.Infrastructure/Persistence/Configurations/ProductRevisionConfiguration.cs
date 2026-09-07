using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Persistence.Converters;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductRevisionConfiguration : IEntityTypeConfiguration<ProductRevision>
{
    public void Configure(EntityTypeBuilder<ProductRevision> builder)
    {
        builder.ToTable("product_revisions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // MÊME CONVERSION QUE LA CLÉ PRIMAIRE DE `Product`, ET MÊME TYPE CLR.
        builder.Property(r => r.ProductId)
            .HasConversion(id => id.Value, value => new ProductId(value))
            .IsRequired();
        builder.Property(r => r.Version).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.ShortDescription).HasMaxLength(500);
        builder.Property(r => r.Description).HasMaxLength(4000);
        builder.Property(r => r.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CategoryId).IsRequired();
        builder.Property(r => r.BrandId);

        builder.Property(r => r.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.Attributes)
            .HasConversion(new AttributesJsonConverter(), new AttributesJsonComparer())
            .HasColumnType("jsonb")
            .HasColumnName("attributes")
            .IsRequired();

        builder.Property(r => r.Tags)
            .HasColumnType("text[]")
            .HasColumnName("tags")
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SubmittedAtUtc);
        builder.Property(r => r.ReviewedAtUtc);
        builder.Property(r => r.PublishedAtUtc);

        // TARIFICATION DE RÉFÉRENCE — COLONNES PLATES, PAS DE TABLE SÉPARÉE.
        builder.OwnsOne(r => r.Pricing, pricing =>
        {
            // BIGINT, PAS NUMERIC (§21, décision D13).
            pricing.Property(p => p.BasePrice)
                .HasColumnName("base_price")
                .HasColumnType("bigint")
                .IsRequired();

            pricing.Property(p => p.CompareAtPrice)
                .HasColumnName("compare_at_price")
                .HasColumnType("bigint");

            pricing.Property(p => p.CostPrice)
                .HasColumnName("cost_price")
                .HasColumnType("bigint");

            pricing.Property(p => p.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3)
                .IsRequired();

            pricing.Property(p => p.TaxIncluded)
                .HasColumnName("tax_included")
                .IsRequired();

            pricing.Property(p => p.TaxRate)
                .HasColumnName("tax_rate")
                .IsRequired();
        });

        builder.Navigation(r => r.Pricing).IsRequired();

        builder.HasOne(r => r.Condition)
            .WithOne()
            .HasForeignKey<ProductCondition>(c => c.RevisionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // La fiche technique du §12, portée par la révision : la modifier est une
        // modification critique au sens du §6.
        builder.HasMany(r => r.Specifications)
            .WithOne()
            .HasForeignKey(g => g.RevisionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Specifications).UsePropertyAccessMode(PropertyAccessMode.Field);

        // UNE SEULE RÉVISION PAR NUMÉRO DE VERSION (§21).
        builder.HasIndex(r => new { r.ProductId, r.Version }).IsUnique();

        // LE SLUG N'EST PAS UNIQUE — SAUF PARMI LES RÉVISIONS PUBLIÉES.
        builder.HasIndex(r => r.Slug)
            .IsUnique()
            .HasFilter("\"Status\" = 'Published'")
            .HasDatabaseName("ux_product_revisions_published_slug");

        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CategoryId);
        builder.HasIndex(r => r.BrandId);
    }
}

internal sealed class ProductConditionConfiguration : IEntityTypeConfiguration<ProductCondition>
{
    public void Configure(EntityTypeBuilder<ProductCondition> builder)
    {
        builder.ToTable("product_conditions");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.RevisionId).IsRequired();

        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.Grade).HasMaxLength(1);
        builder.Property(c => c.Description).HasMaxLength(2000);

        // CES DEUX BOOLÉENS SONT DÉDUITS DU TYPE, ILS NE SONT PAS SAISIS.
        builder.Property(c => c.IsUsed).IsRequired();
        builder.Property(c => c.IsRefurbished).IsRequired();

        builder.Property(c => c.HasOriginalPackaging).IsRequired();
        builder.Property(c => c.HasOriginalAccessories).IsRequired();
        builder.Property(c => c.FunctionalStatus).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(c => c.RefurbishedByType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.RefurbishedBySellerId);
        builder.Property(c => c.RefurbishmentOperations)
            .HasColumnType("text[]")
            .HasColumnName("refurbishment_operations")
            .IsRequired();
        builder.Property(c => c.BatteryHealthPercentage);
        builder.Property(c => c.BatteryReplaced);

        builder.HasMany(c => c.Defects)
            .WithOne()
            .HasForeignKey(d => d.ConditionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Defects).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => c.RevisionId).IsUnique();
        builder.HasIndex(c => c.Type);
    }
}

internal sealed class ProductDefectConfiguration : IEntityTypeConfiguration<ProductDefect>
{
    public void Configure(EntityTypeBuilder<ProductDefect> builder)
    {
        builder.ToTable("product_condition_defects");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.ConditionId).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Location).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
