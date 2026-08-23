using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasConversion(id => id.Value, value => new UserId(value))
            .ValueGeneratedNever();

        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();

        builder.Property(u => u.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(u => u.PhoneNumber)
            .HasConversion(phone => phone.Value, value => PhoneNumber.Create(value).Value)
            .HasColumnName("phone_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.EmailVerified).IsRequired();
        builder.Property(u => u.MfaEnabled).IsRequired();
        builder.Property(u => u.MfaSecret).HasMaxLength(200);
        builder.Property(u => u.SecurityStamp).IsRequired();
        builder.Property(u => u.EmailVerificationTokenHash).HasMaxLength(200);
        builder.Property(u => u.EmailVerificationExpiresOnUtc);
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(200);
        builder.Property(u => u.PasswordResetExpiresOnUtc);

        // Le compteur d'essais est PERSISTÉ : gardé en mémoire, il se remettrait à
        // zéro à chaque redémarrage et ne serait pas partagé entre les cinq hôtes.
        builder.Property(u => u.PasswordResetAttempts).HasDefaultValue(0);

        // Verrouillage du compte. Même raisonnement que ci-dessus, et il pèse
        // encore plus lourd ici : un compteur d'échecs de CONNEXION gardé en
        // mémoire s'effacerait au redémarrage et ne serait pas partagé entre les
        // cinq hôtes — un attaquant alternerait les hôtes pour multiplier son
        // quota, et le verrou ne tomberait jamais.
        //
        // HasDefaultValue(0) pour les comptes existants : sans valeur par défaut,
        // la colonne serait NULL sur toutes les lignes déjà en base et le premier
        // incrément échouerait.
        builder.Property(u => u.FailedLoginAttempts).HasDefaultValue(0);

        // Nullable : « pas de verrou » est un état, pas une date dans le passé.
        builder.Property(u => u.LockedUntilUtc);

        builder.Property(u => u.CreatedOnUtc).IsRequired();

        // Trace du consentement. Nullable : les comptes créés AVANT la mise en place
        // du dispositif n'ont rien accepté — et il faut que cela se voie, plutôt que
        // de leur prêter un accord qu'ils n'ont jamais donné. Ils passeront par
        // l'écran de consentement à leur prochaine connexion.
        builder.Property(u => u.AcceptedTermsVersion).HasMaxLength(40);
        builder.Property(u => u.AcceptedTermsOnUtc);

        // Nulle = l'e-mail n'a pas été vérifié, ou l'a été authentiquement par le
        // titulaire. Renseignée = un administrateur s'est porté garant.
        builder.Property(u => u.EmailVerifiedByAdminOnUtc);

        // Date d'anonymisation. Nulle pour tout compte vivant.
        builder.Property(u => u.DeletedOnUtc);

        // ─────────────────────────────────────────────────────────────────────────
        // UNICITÉ FILTRÉE — SANS ELLE, LA DEUXIÈME SUPPRESSION DE COMPTE ÉCHOUE.
        //
        // Un compte anonymisé reçoit le téléphone factice « 00000000 » (le value object
        // exige 8 à 15 chiffres : on ne peut pas y écrire « anonymisé »). Avec un index
        // unique GLOBAL, le premier compte supprimé s'approprie cette valeur, et toute
        // suppression suivante violerait la contrainte — l'utilisateur recevrait une
        // erreur incompréhensible en tentant d'exercer un droit.
        //
        // L'index exclut donc les comptes supprimés. C'est aussi ce qu'on veut sur le
        // fond : un compte effacé ne doit pas continuer à réserver une adresse e-mail ni
        // un numéro. Quelqu'un qui supprime son compte doit pouvoir se réinscrire plus
        // tard avec les mêmes coordonnées — ce serait absurde de le lui interdire au nom
        // d'un compte qui, précisément, n'existe plus.
        //
        // Filtre en SQL brut (PostgreSQL) : le nom de colonne est en PascalCase entre
        // guillemets doubles, et le statut est stocké en CHAÎNE (HasConversion<string>).
        //
        // REQUIERT UNE MIGRATION :
        //    dotnet ef migrations add AddAccountDeletion \
        //      -p src/Modules/Identity/HBA.Identity.Infrastructure
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Deleted'");

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Deleted'");

        // Les relations vers les enfants (et leurs index sur la FK ombre) sont
        // définies dans leurs propres configurations, pour que la Fche FK soit
        // FK ombre soit créée avant d'être indexée. Les navigations en lecture seule utilisent
        // automatiquement leur champ de stockage (_roleAssignments, _refreshTokens).

        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.RoleIds);
    }
}
