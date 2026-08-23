using HBA.FoodCarts.Domain.Carts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CartAggregate = HBA.FoodCarts.Domain.Carts.FoodCart;

namespace HBA.FoodCarts.Infrastructure.Persistence.Configurations;

internal sealed class FoodCartConfiguration : IEntityTypeConfiguration<CartAggregate>
{
    public void Configure(EntityTypeBuilder<CartAggregate> builder)
    {
        builder.ToTable("food_carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new FoodCartId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.BuyerId).IsRequired();

        // COLONNE OBLIGATOIRE, ET C'EST LA GARANTIE MONO-RESTAURANT.
        //
        // L'ancien panier portait le restaurant sur chaque LIGNE et vérifiait
        // l'unicité en balayant la collection. La règle vivait donc dans un
        // parcours, pas dans le schéma : invisible en base, et fausse au premier
        // chemin d'écriture qui aurait oublié le contrôle. Ici, un panier n'a
        // qu'une colonne pour désigner un établissement.
        builder.Property(c => c.RestaurantId).IsRequired();

        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.PromotionCode).HasMaxLength(64);

        // `IsRequired()` + `Cascade` : une ligne retirée de la collection est
        // SUPPRIMÉE, et non mise à NULL sur une colonne NOT NULL.
        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey("FoodCartId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.BuyerId, c.Status });

        // ═════════════════════════════════════════════════════════════════════
        // UN SEUL PANIER REPAS ACTIF PAR ACHETEUR — LE CODE LE SUPPOSAIT (§5).
        //
        // `GetActiveByBuyerAsync` fait un `FirstOrDefault` SANS TRI : avec deux
        // paniers actifs, l'acheteur en voyait un au hasard.
        //
        // C'EST UNE RÈGLE PLUS FORTE QU'IL N'Y PARAÎT : un panier repas est lié à
        // UN restaurant. « Un seul actif » signifie donc qu'un client qui hésite
        // entre deux restaurants ne peut pas garder deux paniers en parallèle — le
        // second remplace le premier. C'est ce que le code fait déjà ; la base le
        // tient enfin.
        //
        // INDEX PARTIEL : l'historique des paniers `CheckedOut` et `Abandoned`
        // doit rester possible.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(c => c.BuyerId)
            .IsUnique()
            .HasDatabaseName("ux_food_carts_active_buyer")
            .HasFilter("\"Status\" = 'Active'");

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class FoodCartItemConfiguration : IEntityTypeConfiguration<FoodCartItem>
{
    public void Configure(EntityTypeBuilder<FoodCartItem> builder)
    {
        builder.ToTable("food_cart_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.MenuItemId).IsRequired();
        builder.Property(i => i.NameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(500);

        builder.Property(i => i.UnitBaseAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasMany(i => i.Options)
            .WithOne()
            .HasForeignKey("FoodCartItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        // ═════════════════════════════════════════════════════════════════════
        // AUCUN INDEX UNIQUE SUR (PANIER, PLAT), ET SURTOUT PAS D'INDEX FILTRÉ.
        //
        // Le panier de la marketplace en avait un, `("CartId","OfferId") UNIQUE`,
        // qu'il a fallu rendre FILTRÉ sur `Kind = 'Goods'` le jour où les repas
        // sont entrés : toutes les lignes de plat portaient `OfferId = Guid.Empty`
        // et le SECOND plat d'un panier violait la contrainte. Le client recevait
        // une erreur de base de données en cliquant sur « ajouter ».
        //
        // Ici la question ne se pose plus : un même plat DOIT pouvoir figurer
        // deux fois — « riz avec poulet » et « riz sans » — et ce qui les
        // distingue est l'ensemble de leurs options, qui ne s'exprime pas en une
        // colonne. L'unicité est vérifiée en mémoire par `FoodCartItem.Matches`,
        // et c'est le seul endroit où elle peut l'être.
        //
        // L'index non unique sert la lecture du panier, qui charge toujours les
        // lignes par leur panier.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex("FoodCartId");
    }
}

internal sealed class FoodCartItemOptionConfiguration : IEntityTypeConfiguration<FoodCartItemOption>
{
    public void Configure(EntityTypeBuilder<FoodCartItemOption> builder)
    {
        builder.ToTable("food_cart_item_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("FoodCartItemId");
    }
}
