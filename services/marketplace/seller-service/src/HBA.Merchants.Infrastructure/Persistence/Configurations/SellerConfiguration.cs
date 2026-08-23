using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using HBA.Merchants.Domain.Sellers;

namespace HBA.Merchants.Infrastructure.Persistence.Configurations;

internal sealed class SellerConfiguration : IEntityTypeConfiguration<Seller>
{
    public void Configure(EntityTypeBuilder<Seller> builder)
    {
        builder.ToTable("sellers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SellerId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.ShopName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.LogoUrl).HasMaxLength(2000);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.KybStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        // NULLABLE, ET CE N'EST PAS DE L'HISTORIQUE.
        //
        // La colonne ne porte une valeur QUE pendant une suspension : elle dit d'où
        // le compte vient, pour que la levée le rende là plutôt que de le poser en
        // `Active` d'office. Elle est effacée une fois consommée.
        //
        // Les comptes suspendus AVANT son introduction l'auront nulle — voir le
        // repli documenté dans `Seller.LiftSuspension`. Une migration de données ne
        // peut pas deviner un statut qui n'a jamais été écrit.
        builder.Property(s => s.SuspendedFromStatus).HasConversion<string>().HasMaxLength(20);

        // Le motif du refus. Nullable : aucun refus, ou refus antérieur à ce champ.
        // C'est ce texte que le vendeur relira sur sa fiche quand la notification
        // sera loin.
        builder.Property(s => s.KybRejectionReason).HasMaxLength(500);
        builder.Property(s => s.CommissionRate).HasColumnType("numeric(5,4)").IsRequired();
        builder.Property(s => s.Rating).HasColumnType("numeric(3,2)").IsRequired();
        builder.Property(s => s.SalesCount).IsRequired();
        builder.Property(s => s.CreatedOnUtc).IsRequired();

        // PayoutAccount (VO nullable) sérialisé en jsonb. Cast vers le converter
        // non générique car la propriété est nullable.
        builder.Property(s => s.PayoutAccount)
            .HasConversion((ValueConverter)new PayoutAccountJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("payout_account");

        // Metadata société (VO nullable) en jsonb, null par défaut. Comme PayoutAccount,
        // le converter n'est appliqué qu'aux valeurs non nulles ; NULL reste NULL.
        builder.Property(s => s.Metadata)
            .HasConversion((ValueConverter)new SellerCompanyInfoJsonConverter())
            .HasColumnType("jsonb")
            .HasColumnName("metadata");

        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasIndex(s => s.ShopName).IsUnique();

        builder.Ignore(s => s.DomainEvents);
    }
}
