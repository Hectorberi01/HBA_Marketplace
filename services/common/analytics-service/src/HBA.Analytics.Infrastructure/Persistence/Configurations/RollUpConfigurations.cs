using HBA.Analytics.Domain.RollUps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>LES TROIS TABLES DE ROLL-UP. LEUR CLÉ EST LEUR DÉFINITION.</summary>
public sealed class VenteJournaliereVendeurConfiguration : IEntityTypeConfiguration<VenteJournaliereVendeur>
{
    public void Configure(EntityTypeBuilder<VenteJournaliereVendeur> builder)
    {
        builder.ToTable("seller_daily");

        builder.HasKey(ligne => new { ligne.SellerId, ligne.Day, ligne.Currency, ligne.Kind });

        builder.Property(ligne => ligne.Currency).HasMaxLength(3).IsRequired();
        builder.Property(ligne => ligne.Kind).HasMaxLength(NatureDeCommande.LongueurMax).IsRequired();
        builder.Property(ligne => ligne.Revenue).HasPrecision(18, 2);
        builder.Property(ligne => ligne.UpdatedAtUtc).IsRequired();
    }
}

/// <summary>Voir l'encadré de <see cref="VenteJournaliereVendeurConfiguration"/>.</summary>
public sealed class ActiviteJournalierePlateformeConfiguration
    : IEntityTypeConfiguration<ActiviteJournalierePlateforme>
{
    public void Configure(EntityTypeBuilder<ActiviteJournalierePlateforme> builder)
    {
        builder.ToTable("platform_daily");

        builder.HasKey(ligne => new { ligne.Day, ligne.Kind, ligne.Currency });

        builder.Property(ligne => ligne.Kind).HasMaxLength(NatureDeCommande.LongueurMax).IsRequired();
        builder.Property(ligne => ligne.Currency).HasMaxLength(3).IsRequired();
        builder.Property(ligne => ligne.Gmv).HasPrecision(18, 2);
        builder.Property(ligne => ligne.UpdatedAtUtc).IsRequired();
    }
}

/// <summary>Voir l'encadré de <see cref="VenteJournaliereVendeurConfiguration"/>.</summary>
public sealed class InscriptionJournaliereConfiguration : IEntityTypeConfiguration<InscriptionJournaliere>
{
    public void Configure(EntityTypeBuilder<InscriptionJournaliere> builder)
    {
        builder.ToTable("signup_daily");

        builder.HasKey(ligne => new { ligne.Day, ligne.Kind });

        builder.Property(ligne => ligne.Kind).HasMaxLength(NatureDInscription.LongueurMax).IsRequired();
        builder.Property(ligne => ligne.UpdatedAtUtc).IsRequired();
    }
}

/// <summary>Voir l'encadré de <see cref="VenteJournaliereVendeurConfiguration"/>.</summary>
public sealed class AnnulationJournaliereVendeurConfiguration
    : IEntityTypeConfiguration<AnnulationJournaliereVendeur>
{
    public void Configure(EntityTypeBuilder<AnnulationJournaliereVendeur> builder)
    {
        builder.ToTable("seller_cancellation_daily");

        builder.HasKey(ligne => new { ligne.SellerId, ligne.Day, ligne.Currency });

        builder.Property(ligne => ligne.Currency).HasMaxLength(3).IsRequired();
        builder.Property(ligne => ligne.Amount).HasPrecision(18, 2);
        builder.Property(ligne => ligne.UpdatedAtUtc).IsRequired();
    }
}

/// <summary>Voir l'encadré de <see cref="VenteJournaliereVendeurConfiguration"/>.</summary>
public sealed class PaiementJournalierConfiguration : IEntityTypeConfiguration<PaiementJournalier>
{
    public void Configure(EntityTypeBuilder<PaiementJournalier> builder)
    {
        builder.ToTable("payment_daily");

        builder.HasKey(ligne => new { ligne.Day, ligne.Provider, ligne.Currency, ligne.Outcome });

        builder.Property(ligne => ligne.Provider)
            .HasMaxLength(FournisseurDePaiement.LongueurMax).IsRequired();
        builder.Property(ligne => ligne.Currency).HasMaxLength(3).IsRequired();
        builder.Property(ligne => ligne.Outcome)
            .HasMaxLength(IssueDePaiement.LongueurMax).IsRequired();
        builder.Property(ligne => ligne.Amount).HasPrecision(18, 2);
        builder.Property(ligne => ligne.UpdatedAtUtc).IsRequired();
    }
}
