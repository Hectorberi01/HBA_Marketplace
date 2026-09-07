using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Products;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProductId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.SellerId).IsRequired();

        // NULLABLE, ALORS QUE LE §20 ÉCRIT `store_id uuid NOT NULL`.
        builder.Property(p => p.StoreId);

        builder.Property(p => p.Gtin).HasMaxLength(14);
        builder.Property(p => p.Ean).HasMaxLength(14);
        builder.Property(p => p.ProductGroupId);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(p => p.SuspensionReason).HasMaxLength(1000);

        builder.Property(p => p.CurrentRevisionId).IsRequired();
        builder.Property(p => p.PublishedRevisionId);

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();
        builder.Property(p => p.SubmittedAtUtc);
        builder.Property(p => p.ApprovedAtUtc);
        builder.Property(p => p.PublishedAtUtc);
        builder.Property(p => p.ArchivedAtUtc);

        // Enfants de l'agrégat : révisions, variantes, médias.
        builder.HasMany(p => p.Revisions)
            .WithOne()
            .HasForeignKey(r => r.ProductId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Variants)
            .WithOne()
            .HasForeignKey("ProductId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Media)
            .WithOne()
            .HasForeignKey("ProductId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Media).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.SellerId);
        builder.HasIndex(p => p.StoreId);
        builder.HasIndex(p => p.ProductGroupId);

        // INDEX COMPOSITE (SellerId, Status), PAS DEUX INDEX SÉPARÉS (§21).
        builder.HasIndex(p => new { p.SellerId, p.Status });

        // La file de validation admin (§16) lit exactement ceci.
        builder.HasIndex(p => p.Status);

        builder.HasIndex(p => p.PublishedRevisionId);

        // CES DEUX NAVIGATIONS SONT CALCULÉES, PAS STOCKÉES.
        builder.Ignore(p => p.CurrentRevision);
        builder.Ignore(p => p.PublishedRevision);
        builder.Ignore(p => p.DomainEvents);
    }
}
