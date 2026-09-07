using HBA.Deliveries.Domain.Deliveries;
using HBA.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Domain.Deliveries.Delivery>
{
    public void Configure(EntityTypeBuilder<Domain.Deliveries.Delivery> builder)
    {
        // VERROU OPTIMISTE — ISSUE-028.
        builder.UsePostgresRowVersion();
        builder.HorodateLesModifications();

        // CE QUE L'AUDIT DEMANDAIT ICI AURAIT REJETÉ DES COURSES LÉGITIMES.
        builder.ToTable("deliveries", t =>
        {
            t.HasCheckConstraint(
                "ck_deliveries_price_has_currency",
                "\"Price\" IS NULL OR \"Currency\" IS NOT NULL");

            t.HasCheckConstraint(
                "ck_deliveries_earning_has_basis",
                "\"DriverEarning\" IS NULL "
                + "OR (\"Price\" IS NOT NULL AND \"DriverShareRate\" IS NOT NULL)");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DeliveryId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.Reference).HasMaxLength(120).IsRequired();

        // Les énumérations sont stockées en TEXTE, pas en entier.
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(d => d.RequiredProof).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(d => d.AssignedDriverId)
            .HasConversion(
                id => id!.Value.Value,
                value => new DriverId(value));

        // Renseigné pour les seules courses de source externe (invariant tenu par
        // l'agrégat).
        builder.Property(d => d.PartnerId);

        builder.HasIndex(d => new { d.PartnerId, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_partner")
            .HasFilter("\"PartnerId\" IS NOT NULL");

        // Devis d'origine et montant figé.
        builder.Property(d => d.QuoteId);

        // UN DEVIS NE PAIE QU'UNE SEULE COURSE (§5) — ET CE N'EST QUE LE FILET.
        builder.HasIndex(d => d.QuoteId)
            .IsUnique()
            .HasDatabaseName("ux_deliveries_quote")
            .HasFilter("\"QuoteId\" IS NOT NULL");
        builder.Property(d => d.Price).HasPrecision(12, 2);
        builder.Property(d => d.Currency).HasMaxLength(3);

        // Part du livreur, figée à la remise.
        builder.Property(d => d.DriverEarning).HasPrecision(12, 2);
        builder.Property(d => d.DriverShareRate).HasPrecision(5, 4);

        // Le code remis au destinataire.
        builder.Property(d => d.IssuedPin).HasMaxLength(8);

        // Le compteur de tentatives infructueuses.
        builder.Property(d => d.FailedProofAttempts).IsRequired();

        builder.Property(d => d.ScheduledForUtc);

        // Index partiel : la boucle de dispatch cherche les courses programmées
        // dont la fenêtre s'ouvre.
        builder.HasIndex(d => d.ScheduledForUtc)
            .HasFilter("\"ScheduledForUtc\" IS NOT NULL")
            .HasDatabaseName("ix_deliveries_scheduled_for");

        builder.OwnsOne(d => d.Proof, proof =>
        {
            proof.Property(p => p.Kind).HasColumnName("proof_kind").HasConversion<string>().HasMaxLength(20);
            proof.Property(p => p.Value).HasColumnName("proof_value").HasMaxLength(500);
            proof.Property(p => p.CapturedAtUtc).HasColumnName("proof_captured_at_utc");
        });

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.AcceptedAtUtc);
        builder.Property(d => d.PickedUpAtUtc);
        builder.Property(d => d.DeliveredAtUtc);
        builder.Property(d => d.CancelledAtUtc);
        builder.Property(d => d.CancellationReason).HasMaxLength(300);

        // Points de collecte et de remise : objets-valeurs, donc COLONNES de la
        // table des courses.
        builder.OwnsOne(d => d.Pickup, ConfigureStop("pickup"));
        builder.OwnsOne(d => d.Dropoff, ConfigureStop("dropoff"));

        builder.OwnsOne(d => d.Package, package =>
        {
            package.Property(p => p.Description).HasColumnName("package_description").HasMaxLength(300).IsRequired();
            package.Property(p => p.WeightKg).HasColumnName("package_weight_kg").HasPrecision(9, 3);
            package.Property(p => p.IsFragile).HasColumnName("package_is_fragile").IsRequired();
            package.Property(p => p.IsPerishable).HasColumnName("package_is_perishable").IsRequired();
        });

        // L'historique des propositions appartient à la course : il naît et meurt
        // avec elle, et personne ne l'interroge hors de son contexte.
        builder.OwnsMany(d => d.Assignments, assignment =>
        {
            assignment.ToTable("delivery_assignments");
            assignment.WithOwner().HasForeignKey("delivery_id");
            assignment.HasKey(a => a.Id);
            assignment.Property(a => a.Id).ValueGeneratedNever();

            assignment.Property(a => a.DriverId)
                .HasConversion(id => id.Value, value => new DriverId(value))
                .IsRequired();

            assignment.Property(a => a.AttemptNumber).IsRequired();
            assignment.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
            assignment.Property(a => a.OfferedAtUtc).IsRequired();
            assignment.Property(a => a.RespondedAtUtc);
            assignment.Property(a => a.Reason).HasMaxLength(300);

            // Retrouver les propositions faites à un livreur : c'est la requête du
            // tableau de bord « pourquoi ce livreur refuse-t-il autant ? ».
            assignment.HasIndex(a => a.DriverId);
        });

        // INDEX PARTIEL SUR LES COURSES À POURVOIR.
        builder.HasIndex(d => new { d.Status, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_awaiting_driver")
            .HasFilter("\"Status\" IN ('SearchingDriver', 'NoDriverAvailable')");

        // Idempotence de la création : une référence par source.
        builder.HasIndex(d => new { d.Reference, d.Source })
            .IsUnique()
            .HasDatabaseName("ux_deliveries_reference_source");

        // UN LIVREUR N'A QU'UNE COURSE EN COURS — INDEX UNIQUE **PARTIEL**.
        builder.HasIndex(d => d.AssignedDriverId)
            .IsUnique()
            .HasDatabaseName("ux_deliveries_engaged_driver")
            .HasFilter(
                "\"AssignedDriverId\" IS NOT NULL AND \"Status\" IN "
                + "('DriverAccepted', 'ArrivedAtPickup', 'PickedUp', 'InTransit', 'ArrivedAtDropoff')");

        // L'index NON unique d'origine reste : il sert les lectures « les courses
        // de ce livreur », historique compris, que l'index partiel ci-dessus ne
        // peut pas servir puisqu'il ne contient que les courses vivantes.
        builder.HasIndex(d => new { d.AssignedDriverId, d.CreatedAtUtc })
            .HasDatabaseName("ix_deliveries_driver");
    }

    /// <summary>Colonnes d'un point de la course, préfixées par son rôle.</summary>
    private static Action<OwnedNavigationBuilder<Domain.Deliveries.Delivery, DeliveryStop>> ConfigureStop(string prefix)
        => stop =>
        {
            stop.Property(s => s.ContactName).HasColumnName($"{prefix}_contact_name").HasMaxLength(120).IsRequired();
            stop.Property(s => s.Phone).HasColumnName($"{prefix}_phone").HasMaxLength(20).IsRequired();
            stop.Property(s => s.CommuneCode).HasColumnName($"{prefix}_commune_code").HasMaxLength(60).IsRequired();
            stop.Property(s => s.Quartier).HasColumnName($"{prefix}_quartier").HasMaxLength(120);
            stop.Property(s => s.Landmark).HasColumnName($"{prefix}_landmark").HasMaxLength(250).IsRequired();
            stop.Property(s => s.Instructions).HasColumnName($"{prefix}_instructions").HasMaxLength(500);

            // La position est désormais OBLIGATOIRE : la tarification à la distance
            // ne peut pas s'en passer.
            stop.OwnsOne(s => s.Position, position =>
            {
                position.Property(p => p.Latitude).HasColumnName($"{prefix}_latitude").IsRequired();
                position.Property(p => p.Longitude).HasColumnName($"{prefix}_longitude").IsRequired();
            });

            stop.Navigation(s => s.Position).IsRequired();
        };
}
