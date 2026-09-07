using HBA.FoodOrders.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.FoodOrders.Infrastructure.Persistence.Configurations;

internal sealed class MealOrderConfiguration : IEntityTypeConfiguration<MealOrder>
{
    public void Configure(EntityTypeBuilder<MealOrder> builder)
    {
        builder.ToTable("meal_orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new MealOrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.BuyerId).IsRequired();
        builder.Property(o => o.RestaurantId).IsRequired();
        builder.Property(o => o.CartId).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        // Stocké en TEXTE : une commande se lit en base pendant un incident, et «
        // UnderReview » s'y comprend là où « 7 » demande de retrouver
        // l'énumération.
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(o => o.PromotionCode).HasMaxLength(64);
        builder.Property(o => o.CreatedAtUtc).IsRequired();

        builder.Property(o => o.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalSellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalPlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ShippingFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.GrandTotal).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(o => o.DeliveryQuoteId).HasMaxLength(64);
        builder.Property(o => o.CustomerNote).HasMaxLength(500);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.ReviewReason).HasMaxLength(500);
        builder.Property(o => o.UnderReviewSinceUtc);

        // ── Adresse figée ───────────────────────────────────────────────────
        builder.Property(o => o.ShipToLabel).HasMaxLength(60);
        builder.Property(o => o.ShipToRecipient).HasMaxLength(120);
        builder.Property(o => o.ShipToPhone).HasMaxLength(20);
        builder.Property(o => o.ShipToCommuneCode).HasMaxLength(40);
        builder.Property(o => o.ShipToQuartier).HasMaxLength(120);
        builder.Property(o => o.ShipToLandmark).HasMaxLength(200);
        builder.Property(o => o.ShipToLine1).HasMaxLength(200);
        builder.Property(o => o.ShipToCountryCode).HasMaxLength(2);
        builder.Property(o => o.ShipToLatitude);
        builder.Property(o => o.ShipToLongitude);

        // Propriétés CALCULÉES : elles se dérivent, elles ne se stockent pas.
        builder.Ignore(o => o.ShipToCommuneName);

        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("MealOrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // UN PANIER NE PRODUIT QU'UNE COMMANDE — ET C'EST LA BASE QUI LE DIT.
        builder.HasIndex(o => o.CartId).IsUnique();

        builder.HasIndex(o => new { o.BuyerId, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.RestaurantId, o.CreatedAtUtc });

        // Le tri de la file d'arbitrage — voir `MealOrder.UnderReviewSinceUtc`.
        builder.HasIndex(o => o.UnderReviewSinceUtc)
            .HasDatabaseName("ix_meal_orders_under_review")
            .HasFilter("\"UnderReviewSinceUtc\" IS NOT NULL");

        // JETON DE CONCURRENCE (§6) — LA COMMANDE DE REPAS EST TIRÉE DE QUATRE
        // CÔTÉS À LA FOIS.
        builder.UsePostgresRowVersion();

        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class MealOrderLineConfiguration : IEntityTypeConfiguration<MealOrderLine>
{
    public void Configure(EntityTypeBuilder<MealOrderLine> builder)
    {
        builder.ToTable("meal_order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.MenuItemId).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.Quantity).IsRequired();

        builder.Property(l => l.UnitBasePrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.SellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.PlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.FinalUnitPrice).HasColumnType("numeric(18,2)").IsRequired();

        // CALCULÉE : prix final × quantité.
        builder.Ignore(l => l.LineTotal);

        builder.HasMany(l => l.Options)
            .WithOne()
            .HasForeignKey("MealOrderLineId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(l => l.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex("MealOrderId");
    }
}

internal sealed class MealOrderLineOptionConfiguration : IEntityTypeConfiguration<MealOrderLineOption>
{
    public void Configure(EntityTypeBuilder<MealOrderLineOption> builder)
    {
        builder.ToTable("meal_order_line_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("MealOrderLineId");
    }
}
