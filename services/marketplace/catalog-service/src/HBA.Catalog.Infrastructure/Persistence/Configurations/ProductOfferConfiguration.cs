using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Offers;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>Table <c>product_offers</c> — le prix, séparé de la fiche.</summary>
internal sealed class ProductOfferConfiguration : IEntityTypeConfiguration<ProductOffer>
{
    public void Configure(EntityTypeBuilder<ProductOffer> builder)
    {
        builder.ToTable("product_offers");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OfferId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.ProductId).IsRequired();
        builder.Property(o => o.VariantId).IsRequired();
        builder.Property(o => o.StoreId).IsRequired();
        builder.Property(o => o.SellerId).IsRequired();

        builder.OwnsOne(o => o.SellerPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("SellerPriceAmount").HasPrecision(18, 2).IsRequired();
            m.Property(x => x.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(o => o.BuyerPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("BuyerPriceAmount").HasPrecision(18, 2).IsRequired();
            m.Property(x => x.Currency).HasColumnName("BuyerPriceCurrency").HasMaxLength(3).IsRequired();
        });

        builder.OwnsOne(o => o.PromotionalPrice, m =>
        {
            m.Property(x => x.Amount).HasColumnName("PromotionalPriceAmount").HasPrecision(18, 2);
            m.Property(x => x.Currency).HasColumnName("PromotionalPriceCurrency").HasMaxLength(3);
        });

        builder.Property(o => o.CommissionAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.ProviderFeeAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(o => o.PromotionEndsOnUtc);

        // LES ÉNUMÉRATIONS SONT STOCKÉES EN CHAÎNE, PAS EN ENTIER.
        builder.Property(o => o.Condition).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Fulfillment).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(o => o.ShipFromLocationId).IsRequired();
        builder.Property(o => o.HandlingTimeDays).IsRequired();

        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.StatusReason).HasMaxLength(1000);
        builder.Property(o => o.CreatedOnUtc).IsRequired();
        builder.Property(o => o.UpdatedOnUtc);

        // Propriété CALCULÉE : elle dépend de l'heure courante
        // (`IsPromotionRunning`).
        builder.Ignore(o => o.EffectivePrice);

        // UNE BOUTIQUE, UNE VARIANTE, UNE OFFRE.
        builder.HasIndex(o => new { o.StoreId, o.VariantId })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Archived'")
            .HasDatabaseName("ux_product_offers_store_variant");

        builder.HasIndex(o => o.ProductId);
        builder.HasIndex(o => o.VariantId);
        builder.HasIndex(o => o.SellerId);
    }
}
