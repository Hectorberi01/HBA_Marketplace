using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Food.Infrastructure.Persistence.Configurations;

internal sealed class RestaurantConfiguration : IEntityTypeConfiguration<Restaurant>
{
    public void Configure(EntityTypeBuilder<Restaurant> builder)
    {
        // UN ÉTABLISSEMENT SOUMIS À VALIDATION A UN DOSSIER DE REVERSEMENT.
        builder.ToTable("restaurants", t => t.HasCheckConstraint(
            "ck_restaurants_pending_requires_payout",
            "\"Status\" <> 'PendingApproval' OR \"PayoutSellerId\" IS NOT NULL"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new RestaurantId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OwnerUserId).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(150).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.Property(r => r.LogoMediaId);
        builder.Property(r => r.CoverMediaId);

        // TRANSITOIRE — à supprimer une fois les logos reversés dans Media.
        builder.Property(r => r.LegacyLogoUrl).HasMaxLength(500);

        builder.Property(r => r.LogoPublicUrl).HasMaxLength(500);
        builder.Property(r => r.Phone).HasMaxLength(20).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.StatusReason).HasMaxLength(500);

        // ── Réglages commerciaux (§3, §14) ──────────────────────────────────
        builder.Property(r => r.AcceptanceMode).HasConversion<int>().IsRequired();
        builder.Property(r => r.MinimumOrderAmount).HasColumnType("numeric(18,2)");
        builder.Property(r => r.MaximumActiveOrders);
        builder.Property(r => r.BlocksOrdersWhenSaturated).IsRequired();

        // VERROU OPTIMISTE (C13). Le personnel et les commandes en avaient un, pas
        // l'établissement — alors qu'un manager qui règle les horaires pendant
        // qu'un autre change le mode d'acceptation écrasait silencieusement la
        // moitié du travail de l'autre.
        builder.UsePostgresRowVersion();
        builder.Property(r => r.FulfillmentLocationId);

        // Le dossier vendeur qui encaisse.
        builder.Property(r => r.PayoutSellerId);
        builder.Property(r => r.PreparationMinutes).IsRequired();
        builder.Property(r => r.PausedUntilUtc);
        builder.Property(r => r.CreatedOnUtc).IsRequired();
        builder.Property(r => r.UpdatedOnUtc);

        // LES CRÉNEAUX SONT DES LIGNES, PAS UN JSON.
        builder.OwnsMany<ServiceHours>("_serviceHours", hours =>
        {
            hours.ToTable("restaurant_service_hours");
            hours.WithOwner().HasForeignKey("RestaurantId");
            hours.Property<int>("Id").ValueGeneratedOnAdd();
            hours.HasKey("Id");

            hours.Property(h => h.Day).HasConversion<string>().HasMaxLength(10).IsRequired();
            hours.Property(h => h.OpensAt).IsRequired();
            hours.Property(h => h.ClosesAt).IsRequired();

            hours.HasIndex("RestaurantId", "Day");
        });

        // ── Les exceptions datées (§4) ───────────────────────────────────────
        builder.OwnsMany<SpecialOpeningHour>("_specialHours", exceptions =>
        {
            exceptions.ToTable("restaurant_special_hours");
            exceptions.WithOwner().HasForeignKey("RestaurantId");

            // CLÉ SUR (Restaurant, Date) ET NON SUR UN Id TECHNIQUE.
            exceptions.HasKey("RestaurantId", nameof(SpecialOpeningHour.Date));

            exceptions.Property(e => e.Date).IsRequired();
            exceptions.Property(e => e.IsClosed).IsRequired();
            exceptions.Property(e => e.OpensAt);
            exceptions.Property(e => e.ClosesAt);
            exceptions.Property(e => e.Reason).HasMaxLength(200);
        });

        // UN SEUL ÉTABLISSEMENT PAR COMPTE.
        builder.HasIndex(r => r.OwnerUserId).IsUnique();

        // La vitrine liste les établissements EN SERVICE : c'est l'index qui la
        // porte.
        builder.HasIndex(r => r.Status);

        // CHAQUE COLLECTION POSSÉDÉE EXIGE DEUX GESTES : OwnsMany sur le CHAMP
        // PRIVÉ, et Ignore sur la propriété de LECTURE.
        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.ServiceHours);
        builder.Ignore(r => r.SpecialHours);
    }
}

internal sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("menus");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasConversion(id => id.Value, value => new MenuId(value))
            .ValueGeneratedNever();

        builder.Property(m => m.RestaurantId).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Description).HasMaxLength(1000);
        builder.Property(m => m.DisplayOrder).IsRequired();
        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.CreatedOnUtc).IsRequired();
        builder.Property(m => m.UpdatedOnUtc);

        // LE CRÉNEAU DE SERVICE (cahier §5), EN QUATRE COLONNES NULLABLES.
        builder.OwnsOne(m => m.Window, creneau =>
        {
            creneau.Property(w => w.AvailableFrom).HasColumnName("AvailableFrom");
            creneau.Property(w => w.AvailableUntil).HasColumnName("AvailableUntil");
            creneau.Property(w => w.StartTime).HasColumnName("StartTime");
            creneau.Property(w => w.EndTime).HasColumnName("EndTime");

            creneau.Ignore(w => w.IsAlways);
            creneau.Ignore(w => w.WrapsMidnight);
        });

        builder.HasIndex(m => new { m.RestaurantId, m.DisplayOrder });

        builder.Ignore(m => m.DomainEvents);
    }
}

/// <summary>
/// Les SECTIONS de carte — ce qui s'appelait « menus » avant la bascule à deux
/// niveaux, et qui vit désormais dans <c> menu_categories</c>.
/// </summary>
internal sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("menu_categories");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new MenuCategoryId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.RestaurantId).IsRequired();
        builder.Property(c => c.MenuId).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.DisplayOrder).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedOnUtc).IsRequired();
        builder.Property(c => c.UpdatedOnUtc);

        // Deux index, deux usages : la projection de la carte parcourt les sections
        // d'un RESTAURANT ; la garde de suppression compte celles d'une CARTE.
        builder.HasIndex(c => new { c.RestaurantId, c.DisplayOrder });
        builder.HasIndex(c => c.MenuId);

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.RestaurantId).IsRequired();
        builder.Property(i => i.MenuCategoryId).IsRequired();
        builder.Property(i => i.Name).HasMaxLength(150).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(2000);
        builder.Property(i => i.ImageMediaId);
        builder.Property(i => i.LegacyImageUrl).HasMaxLength(500);

        // MÊME PLAFOND QUE `LegacyImageUrl`, ET IL DOIT SUIVRE `ProductMedia.Url`.
        builder.Property(i => i.ImagePublicUrl).HasMaxLength(500);
        builder.Property(i => i.DisplayOrder).IsRequired();
        builder.Property(i => i.CreatedOnUtc).IsRequired();
        builder.Property(i => i.UpdatedOnUtc);

        builder.OwnsOne(i => i.BasePrice, prix =>
        {
            prix.Property(p => p.Amount).HasColumnName("BasePriceAmount").HasColumnType("numeric(18,2)").IsRequired();
            prix.Property(p => p.Currency).HasColumnName("BasePriceCurrency").HasMaxLength(3).IsRequired();
        });

        ConfigureAvailability(builder.OwnsOne(i => i.Availability), "Availability");

        // AUCUN INDEX SUR « DISPONIBLE MAINTENANT ».
        builder.HasIndex(i => new { i.RestaurantId, i.MenuCategoryId, i.DisplayOrder });

        // VERROU OPTIMISTE (C13). Deux personnes qui modifient la même fiche —
        // l'une le prix, l'autre les options — s'écrasaient en silence.
        builder.UsePostgresRowVersion();

        builder.OwnsMany<OptionGroup>("_optionGroups", ConfigureOptionGroups);

        builder.Ignore(i => i.DomainEvents);
        builder.Ignore(i => i.OptionGroups);
    }

    private static void ConfigureOptionGroups(OwnedNavigationBuilder<MenuItem, OptionGroup> groupes)
    {
        groupes.ToTable("menu_option_groups");
        groupes.WithOwner().HasForeignKey("MenuItemId");
        groupes.HasKey(g => g.Id);

        groupes.Property(g => g.Id).ValueGeneratedNever();
        groupes.Property(g => g.Name).HasMaxLength(120).IsRequired();
        groupes.Property(g => g.MinSelections).IsRequired();
        groupes.Property(g => g.MaxSelections).IsRequired();
        groupes.Property(g => g.DisplayOrder).IsRequired();

        groupes.Ignore(g => g.IsRequired);

        groupes.OwnsMany<MenuOption>("_options", options =>
        {
            options.ToTable("menu_options");
            options.WithOwner().HasForeignKey("OptionGroupId");
            options.HasKey(o => o.Id);

            options.Property(o => o.Id).ValueGeneratedNever();
            options.Property(o => o.Name).HasMaxLength(120).IsRequired();

            // Écart de prix : NÉGATIF AUTORISÉ (« sans viande, −300 F »), d'où un
            // numeric signé et aucune contrainte de positivité.
            options.Property(o => o.PriceDelta).HasColumnType("numeric(18,2)").IsRequired();

            ConfigureAvailability(options.OwnsOne(o => o.Availability), "Availability");
        });

        groupes.Ignore(g => g.Options);
    }

    /// <summary>Deux colonnes pour un état à trois valeurs.</summary>
    private static void ConfigureAvailability<TOwner>(
        OwnedNavigationBuilder<TOwner, ItemAvailability> disponibilite, string prefixe)
        where TOwner : class
    {
        disponibilite.Property(a => a.IsMarkedAvailable)
            .HasColumnName($"{prefixe}IsAvailable").IsRequired();

        disponibilite.Property(a => a.UnavailableUntilUtc)
            .HasColumnName($"{prefixe}UntilUtc");

        disponibilite.Ignore(a => a.IsIndefinitelyUnavailable);
    }
}
