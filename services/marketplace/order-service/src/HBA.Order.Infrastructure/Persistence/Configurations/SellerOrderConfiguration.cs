using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Orders.Infrastructure.Persistence.Configurations;

internal sealed class SellerOrderConfiguration : IEntityTypeConfiguration<SellerOrder>
{
    public void Configure(EntityTypeBuilder<SellerOrder> builder)
    {
        // VERROU OPTIMISTE — ET ICI IL EST RÉELLEMENT ÉVALUÉ.
        builder.UsePostgresRowVersion();
        builder.ToTable("seller_orders");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SellerOrderId(value))
            .ValueGeneratedNever();

        // PAS DE CLÉ ÉTRANGÈRE VERS `orders`, ET C'EST DÉLIBÉRÉ.
        builder.Property(s => s.OrderId).IsRequired();
        builder.Property(s => s.SellerId).IsRequired();
        builder.Property(s => s.BuyerId).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();

        // Stocké en TEXTE, comme `Order.Status` : une commande vendeur se relit en
        // base pendant les incidents, et « ReadyForPickup » s'y comprend là où « 3
        // » demande de retrouver l'énumération.
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.ConfirmedAtUtc);
        builder.Property(s => s.PreparingAtUtc);
        builder.Property(s => s.ReadyForPickupAtUtc);
        builder.Property(s => s.HandedOverAtUtc);
        builder.Property(s => s.RefusedAtUtc);

        // 500 comme `CancellationReason` et `ReviewReason` : c'est la même sorte de
        // texte, écrit par un humain et relu par un humain.
        builder.Property(s => s.RefusalReason).HasMaxLength(500);

        // `Restrict` (§8) — c'est ce que le vendeur a reçu l'ordre d'expédier.
        builder.HasMany(s => s.Lines)
            .WithOne()
            .HasForeignKey("SellerOrderId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Propriétés CALCULÉES, dérivées des lignes : sans ces Ignore, EF
        // réclamerait des colonnes pour des valeurs qui n'en ont pas.
        builder.Ignore(s => s.ItemCount);
        builder.Ignore(s => s.Amount);
        builder.Ignore(s => s.IsOpen);
        builder.Ignore(s => s.DomainEvents);

        // UN VENDEUR, UNE PART PAR COMMANDE — ET C'EST LA BASE QUI LE DIT.
        builder.HasIndex(s => new { s.OrderId, s.SellerId }).IsUnique();

        // Le carnet du vendeur, filtré par état : c'est l'écran de travail
        // d'`ORDER_MANAGER`, et il se lit à chaque ouverture de la console.
        builder.HasIndex(s => new { s.SellerId, s.Status });
    }
}

internal sealed class SellerOrderLineConfiguration : IEntityTypeConfiguration<SellerOrderLine>
{
    public void Configure(EntityTypeBuilder<SellerOrderLine> builder)
    {
        builder.ToTable("seller_order_lines");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.OrderLineId).IsRequired();
        builder.Property(l => l.ProductId).IsRequired();

        // NON NULL MAIS POSSIBLEMENT VIDE, comme `OrderLine.Sku` dont elle est la
        // copie.
        builder.Property(l => l.Sku).HasMaxLength(64).IsRequired();

        builder.Property(l => l.ShipFromLocationId).IsRequired();
        builder.Property(l => l.Quantity).IsRequired();
        builder.Property(l => l.UnitPaidAmount).HasColumnType("numeric(18,2)").IsRequired();

        builder.Ignore(l => l.LineTotal);

        builder.HasIndex("SellerOrderId");

        // LA LIGNE D'ORIGINE EST INDEXÉE, ET CE N'EST PAS DÉCORATIF.
        builder.HasIndex(l => l.OrderLineId);
    }
}
