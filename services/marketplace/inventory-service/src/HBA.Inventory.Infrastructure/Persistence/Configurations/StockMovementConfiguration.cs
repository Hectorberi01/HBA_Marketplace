using HBA.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Inventory.Infrastructure.Persistence.Configurations;

/// <summary>
/// AUCUNE CLÉ ÉTRANGÈRE VERS `inventory_items`, ET C'EST DÉLIBÉRÉ.
///
/// Le journal doit survivre à la disparition de l'article. Un vendeur qui retire
/// une référence ne doit pas effacer l'historique des mouvements qui l'ont
/// concernée — c'est précisément quand une ligne disparaît qu'on veut savoir ce
/// qui lui est arrivé. `InventoryItemId` est donc un identifiant nu, indexé, sans
/// contrainte référentielle.
///
/// C'est le même raisonnement que `audit_entries`, qui ne pointe vers rien non plus.
/// </summary>
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
        // en entier. C'est une table qu'on lit à la main quand on cherche à
        // comprendre un écart de stock, et « Adjusted » se lit là où « 1 » se
        // décode.
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

        // Les deux moitiés d'un transfert se retrouvent par leur référence
        // commune. Partiel : la colonne est nulle sur la majorité des lignes.
        builder.HasIndex(m => m.Reference)
            .HasDatabaseName("ix_stock_movements_reference")
            .HasFilter("\"Reference\" IS NOT NULL");
    }
}
