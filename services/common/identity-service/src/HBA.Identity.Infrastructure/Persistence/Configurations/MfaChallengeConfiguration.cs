using HBA.Identity.Domain.Mfa;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HBA.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Mapping de la table <c>mfa_challenges</c> du §10.1.</summary>
public sealed class MfaChallengeConfiguration : IEntityTypeConfiguration<MfaChallenge>
{
    public void Configure(EntityTypeBuilder<MfaChallenge> builder)
    {
        builder.ToTable("mfa_challenges");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.UserId).IsRequired();
        builder.Property(c => c.Channel).HasMaxLength(16).IsRequired();
        builder.Property(c => c.CodeHash).HasMaxLength(256).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.Attempts).IsRequired().HasDefaultValue(0);
        builder.Property(c => c.CreatedOnUtc).IsRequired();
        builder.Property(c => c.ConsumedAtUtc);

        // Index de la requête « défis vivants de cet utilisateur », exécutée à chaque
        // émission. Partiel : les défis consommés — l'écrasante majorité au bout de
        // quelques jours — n'y entrent jamais, donc son coût ne suit pas la taille
        // de la table mais le nombre de codes en circulation, qui reste minuscule.
        builder.HasIndex(c => new { c.UserId, c.ExpiresAtUtc })
            .HasFilter("\"ConsumedAtUtc\" IS NULL")
            .HasDatabaseName("ix_mfa_challenges_active");

        // Purge : un défi consommé ou expiré n'a plus aucune valeur, et la table
        // grossit d'une ligne par tentative de connexion à deux facteurs.
        builder.HasIndex(c => c.ExpiresAtUtc).HasDatabaseName("ix_mfa_challenges_expires_at");

        builder.Ignore(c => c.DomainEvents);
    }
}
