using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Catalog.Domain.Reviews;

namespace HBA.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> builder)
    {
        builder.ToTable("product_reviews");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // `ProductId` EST UN Guid NU, PAS UN `ProductId`, ET IL N'Y A PAS DE
        //    CLÉ ÉTRANGÈRE VERS `products`.
        //
        // C'est un agrégat séparé : il référence le produit par identifiant, comme
        // le fait `ProductOffer`. Une vraie clé étrangère avec cascade
        // supprimerait le journal des décisions en même temps que la fiche — or
        // c'est précisément quand une fiche disparaît qu'on veut savoir qui l'avait
        // approuvée.
        builder.Property(r => r.ProductId).IsRequired();
        builder.Property(r => r.RevisionId).IsRequired();
        builder.Property(r => r.RevisionVersion).IsRequired();
        builder.Property(r => r.SellerId).IsRequired();
        builder.Property(r => r.ReviewedBy).IsRequired();

        builder.Property(r => r.Decision).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Comment).HasMaxLength(2000);
        builder.Property(r => r.ReviewedAtUtc).IsRequired();

        builder.HasMany(r => r.Reasons)
            .WithOne()
            .HasForeignKey(m => m.ReviewId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Reasons).UsePropertyAccessMode(PropertyAccessMode.Field);

        // L'historique d'un produit, du plus récent au plus ancien : c'est la
        // seule lecture de cette table côté fiche.
        builder.HasIndex(r => new { r.ProductId, r.ReviewedAtUtc });

        // « Qu'a fait cet administrateur ? » — la question d'audit.
        builder.HasIndex(r => r.ReviewedBy);

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class ProductReviewReasonConfiguration : IEntityTypeConfiguration<ProductReviewReason>
{
    public void Configure(EntityTypeBuilder<ProductReviewReason> builder)
    {
        builder.ToTable("product_review_reasons");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ReviewId).IsRequired();
        builder.Property(m => m.Code).HasMaxLength(50).IsRequired();
        builder.Property(m => m.Field).HasMaxLength(100);
        builder.Property(m => m.Message).HasMaxLength(1000).IsRequired();
    }
}
