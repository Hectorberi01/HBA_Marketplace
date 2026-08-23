using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using HBA.Catalog.Domain.Products;
using HBA.Catalog.Infrastructure.Persistence.Converters;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        builder.Property(v => v.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(v => v.VariantAttributes)
            .HasConversion(new AttributesJsonConverter(), new AttributesJsonComparer())
            .HasColumnType("jsonb")
            .HasColumnName("variant_attributes")
            .IsRequired();

        builder.Property(v => v.Barcode).HasMaxLength(64);
        builder.Property(v => v.WeightGrams).IsRequired();

        // `HasDefaultValue(true)` EST INDISPENSABLE, ET PAS POUR LE CONFORT.
        //
        // Sans lui, la migration ajoute la colonne avec le défaut du type — `false`
        // — et retire de la vente, silencieusement, TOUTES les déclinaisons déjà en
        // base. Le catalogue se viderait au déploiement, sans erreur ni journal.
        builder.Property(v => v.IsActive).IsRequired().HasDefaultValue(true);

        // Cast vers le type ValueConverter non générique : la propriété est
        // Dimensions? (nullable), l'overload générique infèrerait un converter
        // nullable et rejetterait le nôtre. EF gère la valeur null tout seul.
        builder.Property(v => v.Dimensions)
            .HasConversion((ValueConverter)new DimensionsJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("dimensions");

        // SKU unique globalement (contrat avec Inventory / Pricing).
        builder.HasIndex(v => v.Sku).IsUnique();
    }
}
