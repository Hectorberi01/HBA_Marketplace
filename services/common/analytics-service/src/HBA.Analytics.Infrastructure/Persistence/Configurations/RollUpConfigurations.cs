using HBA.Analytics.Domain.RollUps;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Analytics.Infrastructure.Persistence.Configurations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS TABLES DE ROLL-UP. LEUR CLÉ EST LEUR DÉFINITION.
///
/// Chacune n'a que sa clé primaire pour index, et c'est suffisant : les trois
/// lectures du service filtrent sur un PRÉFIXE de cette clé — (vendeur, jour)
/// pour l'une, (jour) pour les deux autres. Ajouter un index secondaire coûterait
/// une écriture de plus par événement consommé pour une lecture que l'index
/// primaire sert déjà.
///
/// AUCUNE CLÉ ÉTRANGÈRE VERS UN AUTRE SCHÉMA, et il n'y en aura jamais :
/// `SellerId` désigne un vendeur de seller-service, dans une AUTRE base. C'est
/// la règle du §9, et c'est aussi ce qui rend ces tables reconstructibles —
/// elles ne contraignent rien, elles ne font que compter.
///
/// LES MONTANTS SONT EN `numeric(18,2)`, PAS EN VIRGULE FLOTTANTE. Une somme
/// d'argent accumulée en `double` dérive, et la dérive d'un roll-up journalier
/// est cumulative : elle ne se corrige qu'en recalculant tout.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
