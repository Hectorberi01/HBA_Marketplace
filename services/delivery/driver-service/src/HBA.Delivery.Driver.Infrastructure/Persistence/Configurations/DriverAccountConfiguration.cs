using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Drivers.Infrastructure.Persistence.Configurations;

/// <summary>LE DOSSIER LIVREUR EN BASE.</summary>
internal sealed class DriverAccountConfiguration : IEntityTypeConfiguration<DriverAccount>
{
    public void Configure(EntityTypeBuilder<DriverAccount> builder)
    {
        builder.ToTable("driver_accounts", DriverDbContext.SchemaName);
        builder.HasKey(account => account.Id);

        // `ValueGeneratedNever` : l'identifiant est tiré DANS le domaine
        // (`DriverAccount.Register`), et il est repris tel quel par la projection
        // de delivery-service.
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
            // version REMPLACE la précédente (voir `SubmitDocument`).
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
    }
}
