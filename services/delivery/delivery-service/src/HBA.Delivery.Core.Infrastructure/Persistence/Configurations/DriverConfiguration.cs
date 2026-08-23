using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Deliveries.Infrastructure.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new DriverId(value))
            .ValueGeneratedNever();

        builder.Property(d => d.UserId).IsRequired();
        builder.Property(d => d.FullName).HasMaxLength(200).IsRequired();
        builder.Property(d => d.Phone).HasMaxLength(20).IsRequired();

        builder.Property(d => d.Vehicle).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.AccountStatus).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(d => d.Availability).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(d => d.RegisteredAtUtc).IsRequired();
        builder.Property(d => d.VerifiedAtUtc);
        builder.Property(d => d.StatusReason).HasMaxLength(300);
        builder.Property(d => d.LastPositionAtUtc);
        builder.Property(d => d.CompletedDeliveries).IsRequired();

        builder.OwnsOne(d => d.LastKnownPosition, position =>
        {
            position.Property(p => p.Latitude).HasColumnName("last_latitude");
            position.Property(p => p.Longitude).HasColumnName("last_longitude");
        });

        // ─────────────────────────────────────────────────────────────────────
        // UN COMPTE UTILISATEUR = UN SEUL LIVREUR.
        //
        // La contrainte est vérifiée dans RegisterDriverCommandHandler, mais un
        // contrôle applicatif ne survit pas à deux requêtes simultanées : c'est
        // exactement ce que produit un double appui sur « S'inscrire » depuis un
        // réseau lent. La base est le seul endroit où cette règle tient vraiment.
        //
        // Sans elle, la même personne recevrait deux propositions pour la même
        // course, et en refuserait une des deux — le dispatch la compterait comme
        // un refus.
        // ─────────────────────────────────────────────────────────────────────
        builder.HasIndex(d => d.UserId)
            .IsUnique()
            .HasDatabaseName("ux_drivers_user");

        // Le dispatch ne pose qu'une question à cette table : « qui peut recevoir
        // une proposition ? ». L'index la sert directement.
        builder.HasIndex(d => new { d.AccountStatus, d.Availability })
            .HasDatabaseName("ix_drivers_dispatchable");

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE (§6) — LA DISPONIBILITÉ EST ÉCRITE DES DEUX BOUTS.
        //
        // `MarkBusy` vient du dispatch ; `GoOffline`, `TakeBreak` et `GoOnline`
        // viennent du livreur lui-même ; `CompleteMission` vient de la course.
        // Trois producteurs qui n'ont aucune raison de s'attendre. Sans jeton, un
        // livreur pouvait se mettre en pause au moment exact où le dispatch le
        // marquait occupé — et se retrouver `OnBreak` avec une course sur les bras,
        // donc invisible au dispatch tout en la portant.
        //
        // CE N'EST PAS LE MÊME RISQUE QU'ISSUE-028, qui portait sur la course
        // (deux livreurs acceptant la même mission) et s'est fermé par un index
        // unique sur `AssignedDriverId` plus le jeton de `deliveries`. Ici c'est le
        // LIVREUR qu'on protège, pas la course.
        //
        // ET IL FALLAIT VÉRIFIER LA RECOPIE DE POSITION AVANT DE POSER CECI.
        //
        // `RecordPosition` écrit `last_latitude`, `last_longitude` et
        // `LastPositionAtUtc` sur CETTE ligne : le jeton s'y applique donc. Un
        // battement GPS qui croiserait un changement de statut lèverait
        // `DbUpdateConcurrencyException` — sur un chemin de heartbeat, ce serait un
        // 409 rendu à l'application du livreur pour une écriture sans importance.
        //
        // Deux choses l'évitent : la recopie en base est ÉPISODIQUE — Redis porte
        // la donnée chaude que le dispatch lit réellement, et la base ne reçoit
        // qu'un instantané de temps en temps — et le conflit est désormais ABSORBÉ
        // dans `RecordDriverPositionCommandHandler`, où il vaut « la recopie
        // attendra le prochain battement ». Voir l'encadré là-bas.
        //
        // AUCUNE COLONNE N'EST CRÉÉE : `xmin` est une colonne système.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
    }
}
