using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Commerce.Domain.Carts;
using HBA.Shared.Infrastructure.Persistence;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<CartAggregate>
{
    public void Configure(EntityTypeBuilder<CartAggregate> builder)
    {
        builder.ToTable("carts");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CartId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.BuyerId).IsRequired();
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Code promo du panier. Nullable : la plupart des paniers n'en ont pas.
        builder.Property(c => c.PromotionCode).HasMaxLength(64);

        // IsRequired() est indispensable : sans lui, EF considère la relation
        // optionnelle et, à la suppression d'une ligne, tente un « UPDATE
        // cart_items SET CartId = NULL » (sévérance) au lieu d'un DELETE. Comme la
        // colonne CartId est NOT NULL, Postgres rejette → 500.
        builder.HasMany(c => c.Items)
            .WithOne()
            .HasForeignKey("CartId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.BuyerId, c.Status });

        // UN SEUL PANIER ACTIF PAR ACHETEUR — LE CODE LE SUPPOSAIT DÉJÀ (§5).
        builder.HasIndex(c => c.BuyerId)
            .IsUnique()
            .HasDatabaseName("ux_carts_active_buyer")
            .HasFilter("\"Status\" = 'Active'");

        // JETON DE CONCURRENCE — ET IL NE PROTÈGE PAS CE QU'ON CROIT (§6).
        builder.UsePostgresRowVersion();

        // Propriété CALCULÉE : la nature se dérive des lignes.
        builder.Ignore(c => c.Kind);

        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("cart_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        // Le discriminant. Stocké en texte : un panier se lit en base pendant les
        // incidents, et « Food » s'y comprend là où « 1 » demande de retrouver
        // l'énumération.
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(10).IsRequired();

        // ── Marchandise : vides pour une ligne food, donc plus obligatoires ──
        builder.Property(i => i.OfferId).IsRequired();
        builder.Property(i => i.ProductId).IsRequired();
        builder.Property(i => i.CategoryId).IsRequired();
        builder.Property(i => i.SellerId).IsRequired();
        builder.Property(i => i.ShipFromLocationId).IsRequired();

        // LE SKU N'EST PLUS OBLIGATOIRE EN CONTENU, MAIS RESTE NON NUL.
        builder.Property(i => i.Sku).HasMaxLength(64).IsRequired();

        // ── Restauration ────────────────────────────────────────────────────
        builder.Property(i => i.RestaurantId).IsRequired();
        builder.Property(i => i.MenuItemId).IsRequired();
        builder.Property(i => i.Notes).HasMaxLength(500);

        builder.Property(i => i.UnitBaseAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Quantity).IsRequired();

        builder.HasMany(i => i.Options)
            .WithOne()
            .HasForeignKey("CartItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Options).UsePropertyAccessMode(PropertyAccessMode.Field);

        // INDEX UNIQUE FILTRÉ SUR LA SEULE MARCHANDISE.
        builder.HasIndex("CartId", "OfferId")
            .IsUnique()
            .HasFilter("\"Kind\" = 'Goods'");
    }
}

internal sealed class CartItemOptionConfiguration : IEntityTypeConfiguration<CartItemOption>
{
    public void Configure(EntityTypeBuilder<CartItemOption> builder)
    {
        builder.ToTable("cart_item_options");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OptionGroupId).IsRequired();
        builder.Property(o => o.OptionId).IsRequired();

        builder.HasIndex("CartItemId");
    }
}
