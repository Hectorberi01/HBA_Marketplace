using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.Quantity).IsRequired();
        builder.Property(r => r.ExpiresAtUtc).IsRequired();

        // LE STATUT (ISSUE-045).
        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ReservationStatus.Active);

        // `ConfirmedAtUtc`, `ReleasedAtUtc` et `ExpiredAtUtc` sont laissés à la
        // convention : `DateTime?` → colonne nullable, ce qui est exactement ce
        // qu'on veut.

        // La relation et la FK ombre « InventoryItemId » sont définies par le
        // parent (InventoryItemConfiguration) ; EF crée l'index de FK.
        builder.HasIndex(r => r.OrderId);

        // UNE SEULE RÉSERVATION EN COURS PAR (ARTICLE, COMMANDE) — ISSUE-075.
        builder.HasIndex("InventoryItemId", nameof(StockReservation.OrderId))
            .IsUnique()
            .HasDatabaseName("ux_stock_reservations_active_order")
            .HasFilter("\"Status\" = 'Active'");

        // LE BALAYAGE DES RÉSERVATIONS EXPIRÉES N'AVAIT AUCUN INDEX (§4).
        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasDatabaseName("ix_stock_reservations_expiring")
            .HasFilter("\"Status\" = 'Active'");
    }
}
