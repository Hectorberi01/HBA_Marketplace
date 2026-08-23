using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Engagement.Reviews.Domain.Reviews;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Engagement.Reviews.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");
        builder.HorodateLesModifications();

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ReviewId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.ProductId).IsRequired();
        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.OrderId).IsRequired();

        builder.Property(r => r.Rating)
            .HasConversion(rating => rating.Value, value => Rating.Create(value).Value)
            .HasColumnName("rating")
            .IsRequired();

        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Body).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.IsVerifiedPurchase).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SellerReply).HasMaxLength(2000);
        builder.Property(r => r.SellerRepliedAtUtc);

        builder.HasIndex(r => r.ProductId);
        builder.HasIndex(r => new { r.BuyerId, r.ProductId, r.OrderId }).IsUnique();

        // ═════════════════════════════════════════════════════════════════════
        // LE CÔTÉ VENDEUR N'AVAIT AUCUN INDEX, ALORS QUE LE CÔTÉ PRODUIT EN A UN.
        //
        // Deux requêtes filtrent sur `SellerId` : le carnet d'avis du vendeur
        // (`ListBySellerAsync`) et — beaucoup plus chaud — sa NOTE MOYENNE
        // (`GetSellerRatingAsync`), affichée sur chaque fiche et chaque liste
        // d'offres. La seconde balayait toute la table des avis de la plateforme
        // pour en extraire ceux d'un vendeur.
        //
        // `(SellerId, Status)` ET NON `SellerId` SEUL : la note ne compte que les
        // avis `Published`. Avec la seule colonne, PostgreSQL remonte aussi les avis
        // en modération et les rejette ensuite — sur un vendeur populaire, c'est la
        // moitié du travail fait pour rien. Le préfixe `SellerId` sert de toute
        // façon la première requête.
        //
        // CE QUE CET INDEX NE CORRIGE PAS : `GetSellerRatingAsync` charge encore
        // TOUTES les notes en mémoire pour en faire la moyenne, au lieu d'un `AVG()`
        // exécuté par la base. L'index rend la lecture ciblée ; il ne la rend pas
        // bornée. C'est le lot 8.4, et l'index y restera utile.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(r => new { r.SellerId, r.Status });

        builder.Ignore(r => r.DomainEvents);
    }
}
