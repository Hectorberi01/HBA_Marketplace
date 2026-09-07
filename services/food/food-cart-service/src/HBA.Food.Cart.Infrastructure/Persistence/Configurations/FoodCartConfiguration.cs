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

        // UN SEUL PANIER REPAS ACTIF PAR ACHETEUR — LE CODE LE SUPPOSAIT (§5).
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

        // AUCUN INDEX UNIQUE SUR (PANIER, PLAT), ET SURTOUT PAS D'INDEX FILTRÉ.
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
