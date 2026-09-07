using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Stations;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Food.Infrastructure.Persistence.Configurations;

/// <summary>Les postes de préparation (cahier §9).</summary>
internal sealed class PreparationStationConfiguration : IEntityTypeConfiguration<PreparationStation>
{
    public void Configure(EntityTypeBuilder<PreparationStation> builder)
    {
        builder.ToTable("preparation_stations");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new PreparationStationId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.RestaurantId).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(80).IsRequired();
        builder.Property(s => s.Code).HasMaxLength(PreparationStation.MaxCodeLength).IsRequired();
        builder.Property(s => s.DisplayOrder).IsRequired();
        builder.Property(s => s.IsActive).IsRequired();
        builder.Property(s => s.CreatedOnUtc).IsRequired();
        builder.Property(s => s.UpdatedOnUtc);

        // LE CODE EST UNIQUE PAR RESTAURANT, pas globalement : deux maquis ont
        // chacun leur GRILL. Deux postes homonymes dans le MÊME restaurant
        // scinderaient l'écran de cuisine en deux sans que personne comprenne.
        builder.HasIndex(s => new { s.RestaurantId, s.Code })
            .IsUnique()
            .HasDatabaseName("ux_preparation_stations_restaurant_code");

        builder.Ignore(s => s.DomainEvents);
    }
}

/// <summary>LA COMMANDE FOOD ET SON TICKET (§10 à §13).</summary>
internal sealed class FoodOrderConfiguration : IEntityTypeConfiguration<FoodOrder>
{
    public void Configure(EntityTypeBuilder<FoodOrder> builder)
    {
        builder.ToTable("food_orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new FoodOrderId(value))
            .ValueGeneratedNever();

        // EN ENTIER, PAS EN CHAÎNE — CONTRAIREMENT À `PaymentOrderType`.
        builder.Property(o => o.Origin).HasConversion<int>().IsRequired();

        builder.Property(o => o.OrderId).IsRequired();
        builder.Property(o => o.RestaurantId).IsRequired();
        builder.Property(o => o.Status).HasConversion<int>().IsRequired();
        builder.Property(o => o.CustomerNote).HasMaxLength(500);
        builder.Property(o => o.ReceivedAtUtc).IsRequired();
        builder.Property(o => o.AcceptedByUserId);
        builder.Property(o => o.AcceptedAtUtc);
        builder.Property(o => o.StartedAtUtc);
        builder.Property(o => o.ReadyAtUtc);
        builder.Property(o => o.PickedUpAtUtc);
        builder.Property(o => o.EstimatedPreparationMinutes);
        builder.Property(o => o.Priority).IsRequired();

        // UNE COMMANDE COMMERCIALE N'A QU'UNE PART CUISINE — DANS SON UNIVERS.
        builder.HasIndex(o => new { o.Origin, o.OrderId })
            .IsUnique()
            .HasDatabaseName("ux_food_orders_order");

        // L'écran de cuisine et la file d'acceptation lisent tous deux par
        // restaurant et statut : c'est l'index le plus sollicité du module.
        builder.HasIndex(o => new { o.RestaurantId, o.Status });

        // VERROU OPTIMISTE (§20 : « garantir une seule transition de statut valide
        // en cas d'actions concurrentes »).
        builder.UsePostgresRowVersion();

        builder.OwnsOne(o => o.Rejection, refus =>
        {
            refus.Property(r => r.Reason).HasColumnName("RejectionReason").HasConversion<int>();
            refus.Property(r => r.Comment).HasColumnName("RejectionComment").HasMaxLength(500);
            refus.Property(r => r.RejectedByUserId).HasColumnName("RejectedByUserId");
            refus.Property(r => r.RejectedAtUtc).HasColumnName("RejectedAtUtc");
        });

        builder.OwnsMany<FoodOrderItem>("_items", ConfigureItems);

        builder.Ignore(o => o.DomainEvents);
        builder.Ignore(o => o.Items);

        // Propriétés CALCULÉES : elles se dérivent des lignes et n'ont rien à faire
        // en base.
        builder.Ignore(o => o.KitchenStatus);
        builder.Ignore(o => o.Total);
        builder.Ignore(o => o.Stations);
    }

    private static void ConfigureItems(OwnedNavigationBuilder<FoodOrder, FoodOrderItem> lignes)
    {
        lignes.ToTable("food_order_items");
        lignes.WithOwner().HasForeignKey("FoodOrderId");
        lignes.HasKey(i => i.Id);

        lignes.Property(i => i.Id).ValueGeneratedNever();
        lignes.Property(i => i.MenuItemId).IsRequired();
        lignes.Property(i => i.NameSnapshot).HasMaxLength(150).IsRequired();
        lignes.Property(i => i.UnitPrice).HasColumnType("numeric(18,2)").IsRequired();
        lignes.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        lignes.Property(i => i.Quantity).IsRequired();
        lignes.Property(i => i.Notes).HasMaxLength(300);
        lignes.Property(i => i.PreparationStationId);
        lignes.Property(i => i.PreparationMinutes).IsRequired();
        lignes.Property(i => i.Status).HasConversion<int>().IsRequired();

        // L'écran filtré par poste lit ici ; sans index, chaque rafraîchissement
        // parcourt toutes les lignes du service.
        lignes.HasIndex(i => i.PreparationStationId);

        lignes.OwnsMany<FoodOrderItemOption>("_options", options =>
        {
            options.ToTable("food_order_item_options");
            options.WithOwner().HasForeignKey("FoodOrderItemId");
            options.HasKey(o => o.Id);

            options.Property(o => o.Id).ValueGeneratedNever();
            options.Property(o => o.OptionId).IsRequired();
            options.Property(o => o.GroupName).HasMaxLength(120).IsRequired();
            options.Property(o => o.OptionName).HasMaxLength(120).IsRequired();

            // numeric SIGNÉ : « sans viande, −300 F » est une remise légitime.
            options.Property(o => o.PriceDelta).HasColumnType("numeric(18,2)").IsRequired();
        });

        lignes.Ignore(i => i.Options);
        lignes.Ignore(i => i.LineTotal);
    }
}
