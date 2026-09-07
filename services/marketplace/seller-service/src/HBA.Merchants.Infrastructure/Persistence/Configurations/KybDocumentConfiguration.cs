using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Infrastructure.Persistence.Configurations;

internal sealed class KybDocumentConfiguration : IEntityTypeConfiguration<KybDocument>
{
    public void Configure(EntityTypeBuilder<KybDocument> builder)
    {
        builder.ToTable("kyb_documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(d => d.MediaId).IsRequired();

        // TRANSITOIRE — à supprimer une fois les pièces reversées dans Media.
        builder.Property(d => d.LegacyFileUrl).HasMaxLength(2000);
        builder.Property(d => d.UploadedOnUtc).IsRequired();
        builder.Property(d => d.VerifiedAtUtc);

        // Relation vers Seller : la FK ombre « SellerId » est créée ici (avant
        // l'index) et prend le type de la clé principale (SellerId -> uuid).
        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE
        // EF.
        builder.HasOne<Seller>()
            .WithMany(s => s.KybDocuments)
            .HasForeignKey("SellerId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex("SellerId");

        // Propriété CALCULÉE : elle se dérive du MediaId et n'a rien en base.
        builder.Ignore(d => d.IsLegacy);
    }
}
