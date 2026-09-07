using HBA.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>AUCUNE CLÉ ÉTRANGÈRE VERS `inventory_items`, ET C'EST DÉLIBÉRÉ.</summary>
internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.InventoryItemId).IsRequired();
        builder.Property(m => m.Sku).HasMaxLength(64).IsRequired();
        builder.Property(m => m.LocationId).IsRequired();

        // En TEXTE, contrairement au reste de ce schéma qui stocke ses énumérations
        // en entier.
        builder.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(m => m.Delta).IsRequired();
        builder.Property(m => m.OnHandAfter).IsRequired();
        builder.Property(m => m.ActorUserId);
        builder.Property(m => m.Reason).HasMaxLength(200);
        builder.Property(m => m.Reference).HasMaxLength(100);
        builder.Property(m => m.OccurredOnUtc).IsRequired();

        // « Qu'est-il arrivé à CET article » — la lecture du vendeur, du plus
        // récent au plus ancien.
        builder.HasIndex(m => new { m.InventoryItemId, m.OccurredOnUtc })
            .HasDatabaseName("ix_stock_movements_item");

        // « Qu'est-il arrivé à CETTE référence, tous lieux confondus » — la
        // question qu'on pose quand un SKU ne tombe pas juste et qu'on ne sait pas
        // encore dans quel entrepôt chercher.
        builder.HasIndex(m => new { m.Sku, m.OccurredOnUtc })
            .HasDatabaseName("ix_stock_movements_sku");

        // Les deux moitiés d'un transfert se retrouvent par leur référence commune.
        builder.HasIndex(m => m.Reference)
            .HasDatabaseName("ix_stock_movements_reference")
            .HasFilter("\"Reference\" IS NOT NULL");
    }
}
