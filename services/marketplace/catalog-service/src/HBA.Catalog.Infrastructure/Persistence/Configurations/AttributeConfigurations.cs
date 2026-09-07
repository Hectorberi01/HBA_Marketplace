using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Attributes;
using HBA.Catalog.Domain.Brands;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> builder)
    {
        builder.ToTable("attribute_definitions");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Code).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.Unit).HasMaxLength(20);

        builder.Property(a => a.Options)
            .HasColumnType("text[]")
            .HasColumnName("options")
            .IsRequired();

        builder.Property(a => a.CreatedAtUtc).IsRequired();

        // LE CODE EST UNIQUE, ET C'EST TOUTE LA RAISON D'ÊTRE DE CETTE TABLE.
        builder.HasIndex(a => a.Code).IsUnique();

        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class CategoryAttributeConfiguration : IEntityTypeConfiguration<CategoryAttribute>
{
    public void Configure(EntityTypeBuilder<CategoryAttribute> builder)
    {
        builder.ToTable("category_attributes");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.CategoryId).IsRequired();
        builder.Property(a => a.AttributeDefinitionId).IsRequired();
        builder.Property(a => a.Required).IsRequired();
        builder.Property(a => a.Variant).IsRequired();
        builder.Property(a => a.DisplayOrder).IsRequired();

        // UN ATTRIBUT NE SE RATTACHE QU'UNE FOIS À UNE CATÉGORIE.
        builder.HasIndex(a => new { a.CategoryId, a.AttributeDefinitionId }).IsUnique();

        builder.HasIndex(a => a.CategoryId);
    }
}

internal sealed class BrandRequestConfiguration : IEntityTypeConfiguration<BrandRequest>
{
    public void Configure(EntityTypeBuilder<BrandRequest> builder)
    {
        builder.ToTable("brand_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.BrandId);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        builder.Property(r => r.ReviewedBy);
        builder.Property(r => r.RequestedAtUtc).IsRequired();
        builder.Property(r => r.ReviewedAtUtc);

        // La file d'attente de l'administrateur, triée du plus ancien au plus
        // récent.
        builder.HasIndex(r => new { r.Status, r.RequestedAtUtc });

        // INDEX PARTIEL SUR LES DEMANDES EN ATTENTE, PAS UNE CONTRAINTE UNIQUE.
        builder.HasIndex(r => new { r.SellerId, r.Name })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("ux_brand_requests_pending");

        builder.Ignore(r => r.DomainEvents);
    }
}
