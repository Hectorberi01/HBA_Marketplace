using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Drivers.Infrastructure.Persistence.Configurations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DOSSIER LIVREUR EN BASE.
///
/// L'INDEX UNIQUE SUR `UserId` EST LA VRAIE CORRECTION D'ISSUE-029.
///
/// Le contrôle applicatif de `RegisterDriverCommandHandler` ne suffit pas : deux
/// inscriptions concurrentes du même compte — double-clic, réessai du client
/// mobile après une échéance — le passent toutes les deux, et la plateforme se
/// retrouve avec deux dossiers pour une personne. Lequel des deux l'exploitation
/// vérifierait-elle ? Lequel la route `/me` rendrait-elle ? C'est la base qui
/// tranche, et c'est le seul arbitre qui voie les deux écritures.
///
/// CET INDEX N'EST PAS PARTIEL, CONTRAIREMENT À `ux_deliveries_engaged_driver`.
///
/// Là-bas, la contrainte ne vaut que pour les états ENGAGÉS, parce qu'un livreur a
/// évidemment le droit d'avoir livré mille courses. Ici, la règle est
/// inconditionnelle et le restera : un compte HBA n'a qu'un dossier livreur, quel
/// que soit son état — refusé, suspendu ou vérifié. Un dossier refusé se redépose,
/// il ne se recrée pas.
///
/// LES ÉNUMÉRATIONS SONT PERSISTÉES EN TOUTES LETTRES.
///
/// Un entier en base rend toute lecture SQL d'exploitation illisible — et
/// surtout, réordonner une énumération en C# réinterprète silencieusement toutes
/// les lignes déjà écrites. Le coût est quelques octets par ligne sur une table
/// qui compte des milliers de lignes, pas des millions.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DriverAccountConfiguration : IEntityTypeConfiguration<DriverAccount>
{
    public void Configure(EntityTypeBuilder<DriverAccount> builder)
    {
        builder.ToTable("driver_accounts", DriverDbContext.SchemaName);
        builder.HasKey(account => account.Id);

        // `ValueGeneratedNever` : l'identifiant est tiré DANS le domaine
        // (`DriverAccount.Register`), et il est repris tel quel par la projection
        // de delivery-service. Laisser EF croire que la base le génère ferait
        // écrire un identifiant différent de celui que l'événement a annoncé —
        // deux identités pour un même livreur. Même réglage que
        // `DeliveryConfiguration` sur ses propositions.
        builder.Property(account => account.Id).ValueGeneratedNever();

        builder.Property(account => account.UserId).IsRequired();
        builder.HasIndex(account => account.UserId).IsUnique().HasDatabaseName("ux_driver_accounts_user");

        builder.Property(account => account.FullName).IsRequired().HasMaxLength(160);
        builder.Property(account => account.Phone).IsRequired().HasMaxLength(20);
        builder.Property(account => account.StatusReason).HasMaxLength(500);

        builder.Property(account => account.VerificationStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Retrouver la file de vérification sans balayer la table : c'est la
        // lecture que l'exploitation fait à chaque ouverture de sa console.
        builder.HasIndex(account => account.VerificationStatus)
            .HasDatabaseName("ix_driver_accounts_status");

        // COLLECTIONS POSSÉDÉES ET NON ENTITÉS INDÉPENDANTES.
        //
        // Une pièce et un véhicule n'ont aucune vie hors du dossier : rien ne les
        // charge seuls, rien ne les référence, et les supprimer avec lui est la
        // seule conduite correcte. `OwnsMany` donne exactement cela — cascade de
        // suppression et chargement systématique avec le parent — sans qu'aucun
        // appelant puisse les obtenir sans passer par l'agrégat.
        //
        // CONSÉQUENCE À CONNAÎTRE : elles sont TOUJOURS chargées. C'est voulu ;
        // l'agrégat en a besoin pour juger de la complétude du dossier, et une
        // lecture qui n'en voudrait pas rendrait `SubmitForReview` faux.
        builder.OwnsMany(account => account.Documents, document =>
        {
            document.ToTable("driver_documents", DriverDbContext.SchemaName);
            document.WithOwner().HasForeignKey(item => item.DriverId);
            document.HasKey(item => item.Id);
            document.Property(item => item.Id).ValueGeneratedNever();

            document.Property(item => item.Type).HasConversion<string>().HasMaxLength(40).IsRequired();
            document.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            document.Property(item => item.ObjectKey).IsRequired().HasMaxLength(400);
            document.Property(item => item.RejectionReason).HasMaxLength(500);

            // Une seule pièce par type et par dossier : le dépôt d'une nouvelle
            // version REMPLACE la précédente (voir `SubmitDocument`). Sans cette
            // contrainte, un rejeu de la requête empilerait deux exemplaires, et le
            // vérificateur validerait l'un des deux au hasard.
            document.HasIndex(item => new { item.DriverId, item.Type })
                .IsUnique()
                .HasDatabaseName("ux_driver_documents_type");
        });

        builder.OwnsMany(account => account.Vehicles, vehicle =>
        {
            vehicle.ToTable("driver_vehicles", DriverDbContext.SchemaName);
            vehicle.WithOwner().HasForeignKey(item => item.DriverId);
            vehicle.HasKey(item => item.Id);
            vehicle.Property(item => item.Id).ValueGeneratedNever();

            vehicle.Property(item => item.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            vehicle.Property(item => item.Make).HasMaxLength(80);
            vehicle.Property(item => item.Model).HasMaxLength(80);
            vehicle.Property(item => item.Plate).HasMaxLength(20);
            vehicle.Property(item => item.CapacityKg).HasPrecision(8, 2);

            vehicle.HasIndex(item => item.DriverId).HasDatabaseName("ix_driver_vehicles_driver");
        });

        // PAS DE `UsePropertyAccessMode(Field)` ICI, ET CE N'EST PAS UN OUBLI.
        //
        // `Documents` et `Vehicles` n'exposent qu'une vue en lecture seule
        // (`_documents.AsReadOnly()`), donc EF DOIT passer par le champ. Il le
        // fait par convention — le champ `_documents` correspond à la propriété
        // `Documents` —, exactement comme `DeliveryConfiguration` avec
        // `_assignments`. Le déclarer à la main ici et pas là-bas ferait croire
        // que les deux modules ne suivent pas la même règle.
    }
}
