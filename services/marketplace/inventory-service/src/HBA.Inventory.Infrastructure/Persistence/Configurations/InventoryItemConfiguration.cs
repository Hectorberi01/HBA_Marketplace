using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {

        // VERROU OPTIMISTE — SURVENTE.
        builder.UsePostgresRowVersion();
        builder.ToTable("inventory_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasConversion(id => id.Value, value => new InventoryItemId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .HasColumnName("sku")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(i => i.LocationId).IsRequired();
        builder.Property(i => i.OnHand).IsRequired();
        builder.Property(i => i.ReorderThreshold).IsRequired();

        // Le compteur qui rend le verrou effectif (voir l'encadré en tête de
        // méthode).
        builder.Property(i => i.StockVersion).IsRequired().HasDefaultValue(0);

        // Reserved et Available sont calculés à partir des réservations.
        builder.Ignore(i => i.Reserved);
        builder.Ignore(i => i.Available);
        builder.Ignore(i => i.IsLowStock);

        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasMany(i => i.Reservations)
            .WithOne()
            .HasForeignKey("InventoryItemId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(i => i.Reservations).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(i => i.Sku);
        builder.HasIndex(i => new { i.Sku, i.LocationId }).IsUnique();

        builder.Ignore(i => i.DomainEvents);
    }
}
