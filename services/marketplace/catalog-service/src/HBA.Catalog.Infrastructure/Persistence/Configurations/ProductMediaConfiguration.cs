using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductMediaConfiguration : IEntityTypeConfiguration<ProductMedia>
{
    public void Configure(EntityTypeBuilder<ProductMedia> builder)
    {
        builder.ToTable("product_media");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // LA VÉRITÉ. Zéro pour une ligne d'avant la bascule — voir la migration
        // `RepriseImagesProduitVersMedia`.
        builder.Property(m => m.MediaId).IsRequired();

        // La copie de lecture. Obligatoire : une image sans adresse ne s'affiche
        // nulle part, et pour une ligne héritée c'est la seule chose qui reste.
        builder.Property(m => m.Url).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.AltText).HasMaxLength(500);
        builder.Property(m => m.Position).IsRequired();
        builder.Property(m => m.IsPrimary).IsRequired();
        builder.Property(m => m.LegacyExternalId).HasMaxLength(200);

        // Propriété CALCULÉE : elle se dérive du MediaId et n'a rien en base.
        // Sans cet Ignore, EF réclamerait une colonne « IsLegacy ».
        builder.Ignore(m => m.IsLegacy);
    }
}
