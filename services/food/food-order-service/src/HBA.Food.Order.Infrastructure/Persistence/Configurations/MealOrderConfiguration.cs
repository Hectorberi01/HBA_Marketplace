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

        // Stocké en TEXTE : une commande se lit en base pendant un incident, et
        // « UnderReview » s'y comprend là où « 7 » demande de retrouver
        // l'énumération. C'est aussi ce qui protège d'un décalage de valeurs si
        // un état venait à s'insérer au milieu.
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

        // ═════════════════════════════════════════════════════════════════════
        // UN PANIER NE PRODUIT QU'UNE COMMANDE — ET C'EST LA BASE QUI LE DIT.
        //
        // `POST /api/orders` n'avait aucune idempotence, et rien dans le schéma
        // ne s'y opposait : aucune contrainte d'unicité sur `CartId`. Un
        // double-clic, un réseau lent suivi d'un renvoi, ou un rejeu de requête
        // créait DEUX commandes sur le même panier — donc deux paiements.
        //
        // La lecture préalable de `GetByCartAsync` suffit dans le cas courant,
        // mais elle ne voit pas deux requêtes SIMULTANÉES : les deux lisent
        // « aucune commande » avant que l'une ait écrit. Seul l'index unique
        // ferme cette course, et il la ferme du bon côté — la seconde insertion
        // échoue, plutôt que d'encaisser deux fois.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(o => o.CartId).IsUnique();

        builder.HasIndex(o => new { o.BuyerId, o.CreatedAtUtc });
        builder.HasIndex(o => new { o.RestaurantId, o.CreatedAtUtc });

        // Le tri de la file d'arbitrage — voir `MealOrder.UnderReviewSinceUtc`.
        // Index PARTIEL : la file est minuscule à côté de la table, et un index
        // complet paierait chaque commande normale pour servir les rares
        // bloquées.
        builder.HasIndex(o => o.UnderReviewSinceUtc)
            .HasDatabaseName("ix_meal_orders_under_review")
            .HasFilter("\"UnderReviewSinceUtc\" IS NOT NULL");

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE (§6) — LA COMMANDE DE REPAS EST TIRÉE DE QUATRE
        //     CÔTÉS À LA FOIS.
        //
        // Onze transitions écrivent `Status` sur cette ligne, et elles ne viennent
        // pas du même endroit : le client annule, le restaurant refuse, le
        // paiement confirme, la livraison met en arbitrage. Ce sont quatre
        // producteurs indépendants — trois d'entre eux arrivent par Kafka, donc
        // sans aucune sérialisation entre eux.
        //
        // Sans jeton, une annulation client et une confirmation de paiement
        // simultanées se terminaient au dernier écrivain : la commande pouvait
        // rester `Confirmed` après une annulation acceptée, ou l'inverse. Le
        // second reçoit désormais 409, et le message Kafka est rejoué — c'est-à-
        // dire relu sur l'état à jour, ce qui est le comportement voulu.
        //
        // Les transitions écrivent toutes une colonne de CETTE table : le jeton
        // n'est pas inerte. C'est la vérification qu'exige l'encadré
        // d'`UsePostgresRowVersion`.
        //
        // AUCUNE COLONNE N'EST CRÉÉE : `xmin` est une colonne système.
        // ═════════════════════════════════════════════════════════════════════
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

        // CALCULÉE : prix final × quantité. La stocker donnerait deux vérités à
        // tenir d'accord.
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
