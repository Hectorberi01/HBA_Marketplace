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
        //
        // Sans cette contrainte, `color`, `couleur` et `Colour` coexistent selon qui
        // a rempli le formulaire — trois filtres de vitrine au lieu d'un, et une
        // recherche par couleur qui ne trouve qu'un tiers du catalogue. La
        // normalisation en minuscules se fait dans `AttributeDefinition.Create` ;
        // l'index la rend obligatoire.
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
        //
        // Deux lignes pour le même couple donneraient deux fois le même champ dans
        // le formulaire vendeur, avec des caractères obligatoires potentiellement
        // contradictoires — et la validation lirait celui que la base rend en
        // premier, donc pas toujours le même.
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

        // La file d'attente de l'administrateur, triée du plus ancien au plus récent.
        builder.HasIndex(r => new { r.Status, r.RequestedAtUtc });

        // INDEX PARTIEL SUR LES DEMANDES EN ATTENTE, PAS UNE CONTRAINTE UNIQUE.
        //
        // Un même vendeur ne doit pas avoir deux demandes VIVANTES pour le même nom
        // — le double-clic sur le formulaire suffit à les produire. Mais il doit
        // pouvoir redemander après un refus, une fois le motif corrigé. Une contrainte
        // unique simple le lui interdirait pour toujours.
        //
        // Même forme que `ux_product_revisions_published_slug` et
        // `ux_coupon_usages_live_hold` : l'unicité ne vaut que sur l'état actif.
        builder.HasIndex(r => new { r.SellerId, r.Name })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("ux_brand_requests_pending");

        builder.Ignore(r => r.DomainEvents);
    }
}
