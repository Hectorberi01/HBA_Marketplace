using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Orders.Domain.Orders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Orders.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {

        // VERROU OPTIMISTE — la commande est mutée par la saga (paiement,
        // expédition, livraison) et par l'acheteur (annulation).
        builder.UsePostgresRowVersion();
        builder.HorodateLesModifications();

        // UNE COMMANDE PAYÉE PORTE SON PAIEMENT — EN BASE, PAS SEULEMENT EN
        // MÉMOIRE.
        builder.ToTable("orders", t => t.HasCheckConstraint(
            "ck_orders_paid_requires_payment",
            "\"Status\" NOT IN ('Paid', 'Confirmed', 'Delivered', 'UnderReview') "
            + "OR \"PaymentId\" IS NOT NULL"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.BuyerId).IsRequired();
        builder.Property(o => o.CartId).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();

        // Code promo figé au checkout (snapshot).
        builder.Property(o => o.PromotionCode).HasMaxLength(64);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalSellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.TotalPlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.ShippingFee).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(o => o.GrandTotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(o => o.PaymentId);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // L'ARBITRAGE : POURQUOI, ET DEPUIS QUAND.
        builder.Property(o => o.ReviewReason).HasMaxLength(500);
        builder.Property(o => o.UnderReviewSinceUtc);

        // Adresse de livraison figée (snapshot, toutes colonnes nullables).
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

        // Résolus depuis BeninGeography à l'affichage : rien à stocker.
        builder.Ignore(o => o.ShipToCommuneName);
        builder.Ignore(o => o.HasShipToCoordinates);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // CE QUE LES RETOURS ONT RETIRÉ À CETTE COMMANDE (ISSUE-014).
        builder.HasMany(o => o.ReturnSettlements)
            .WithOne()
            .HasForeignKey("OrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(o => o.ReturnSettlements).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Somme des dossiers, recalculée à chaque lecture.
        builder.Ignore(o => o.RefundedAmount);

        builder.HasIndex(o => new { o.BuyerId, o.Status });

        // UN PANIER NE PRODUIT QU'UNE COMMANDE — ET C'EST LA BASE QUI LE DIT.
        builder.HasIndex(o => o.CartId).IsUnique();

        // Propriétés CALCULÉES, dérivées des lignes.
        builder.Property(o => o.DeliveryQuoteId).HasMaxLength(64);

        builder.Ignore(o => o.Kind);
        builder.Ignore(o => o.RestaurantId);
        builder.Ignore(o => o.HasShippingAddress);

        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // Le discriminant, stocké en texte : une commande se relit en base pendant
        // les incidents, et « Food » s'y comprend là où « 1 » demande de retrouver
        // l'énumération.
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();

        // ── Marchandise : vides pour une ligne de repas ─────────────────────
        builder.Property(l => l.OfferId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();
        builder.Property(l => l.SellerId).IsRequired();

        // NON NULL MAIS POSSIBLEMENT VIDE. Une ligne de repas porte la chaîne vide
        // : la colonne garde sa contrainte, et c'est `Kind` qui dit s'il faut la
        // lire.
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();
        builder.Property(l => l.ShipFromLocationId).IsRequired();

        // ── Restauration ────────────────────────────────────────────────────
        builder.Property(l => l.RestaurantId).IsRequired();
        builder.Property(l => l.MenuItemId).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(500);

        // `Restrict` — LE SECOND NIVEAU DE LA CHAÎNE `orders → order_lines →
        // order_line_options`.
        builder.HasMany(l => l.Options)
            .WithOne()
            .HasForeignKey("OrderLineId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(l => l.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitBasePrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.SellerDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.PlatformDiscount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(l => l.FinalUnitPrice).HasColumnType("numeric(18,2)").IsRequired();

        // Propriétés CALCULÉES : sans ces Ignore, EF réclamerait des colonnes.
        builder.Ignore(l => l.LineTotal);
        builder.Ignore(l => l.RequiresStockReservation);

        builder.HasIndex("OrderId");
        builder.HasIndex(l => l.SellerId);

        // L'ADAPTATEUR VERS FOOD CHERCHE PAR RESTAURANT.
        builder.HasIndex(l => l.RestaurantId).HasFilter("\"Kind\" = 'Food'");
    }
}

internal sealed class OrderLineOptionConfiguration : IEntityTypeConfiguration<OrderLineOption>
{
    public void Configure(EntityTypeBuilder<OrderLineOption> builder)
    {
        builder.ToTable("order_line_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("OrderLineId");
    }
}

internal sealed class OrderReturnSettlementConfiguration : IEntityTypeConfiguration<OrderReturnSettlement>
{
    public void Configure(EntityTypeBuilder<OrderReturnSettlement> builder)
    {
        builder.ToTable("order_return_settlements");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ReturnRequestId).IsRequired();
        builder.Property(s => s.RefundedAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.RecordedAtUtc).IsRequired();
        builder.Property(s => s.LastSeenAtUtc).IsRequired();

        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey("OrderReturnSettlementId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // UN DOSSIER, UNE LIGNE — ET C'EST LA BASE QUI LE DIT.
        builder.HasIndex("OrderId", "ReturnRequestId").IsUnique();
    }
}

internal sealed class OrderReturnSettlementLineConfiguration : IEntityTypeConfiguration<OrderReturnSettlementLine>
{
    public void Configure(EntityTypeBuilder<OrderReturnSettlementLine> builder)
    {
        builder.ToTable("order_return_settlement_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OrderItemId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();

        builder.HasIndex("OrderReturnSettlementId");
    }
}
