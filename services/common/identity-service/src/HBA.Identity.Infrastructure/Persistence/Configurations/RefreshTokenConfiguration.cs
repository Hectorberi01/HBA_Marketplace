using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ExpiresOnUtc).IsRequired();
        builder.Property(t => t.CreatedOnUtc).IsRequired();
        builder.Property(t => t.RevokedOnUtc);

        // REQUIS, ET DONC NOT NULL EN BASE.
        //
        // Une colonne nullable ici ferait du step-up du §37 une passoire silencieuse :
        // un jeton sans `AuthenticatedAtUtc` produirait un `auth_time` absent, et
        // `HasRecentAuthentication` refuserait — le vendeur ressaisirait son mot de
        // passe en boucle sans jamais passer, et personne ne saurait pourquoi. La
        // migration remplit les lignes existantes plutôt que d'accepter des trous.
        builder.Property(t => t.AuthenticatedAtUtc).IsRequired();
        builder.Property(t => t.AuthMethods).HasMaxLength(64).IsRequired();

        // Relation vers User : la FK ombre « UserId » est créée ici (avant l'index)
        // et prend le type de la clé principale (UserId, mappé en uuid).
        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
        // ne sévrait PAS pour autant. Le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne aussi le sort des ORPHELINS — avec Cascade, un enfant retiré de la
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production l'ont confirmé.
        //
        // Alors pourquoi IsRequired() ? Pour trois raisons plus modestes et plus sûres :
        //
        //   1. Cette clé étrangère est RÉELLEMENT obligatoire — un enfant sans parent n'a
        //      aucun sens métier. Le modèle le déclarait facultatif. Un modèle qui ment
        //      finit toujours par produire du code qui se trompe.
        //
        //   2. La colonne était NULL-able en base, donc RIEN ne l'interdisait. Une ligne
        //      orpheline a d'ailleurs été trouvée en production (message_reactions) : on
        //      ignore ce qui l'a créée, et c'est précisément le problème. NOT NULL l'aurait
        //      refusée, quelle que soit sa provenance.
        //
        //   3. Sans ça, le comportement dépend d'un réglage FRAGILE : retirer le
        //      `OnDelete(Cascade)` — geste anodin en apparence — ferait réellement basculer
        //      cette relation en sévérance. Avec IsRequired() ET NOT NULL, c'est impossible.
        builder.HasOne<User>()
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey("UserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // ═════════════════════════════════════════════════════════════════════
        // UNIQUE (§5) — DEUX SESSIONS NE PEUVENT PAS PARTAGER UN JETON.
        //
        // L'index existait, sans `IsUnique()`. Rien n'empêchait donc deux lignes de
        // même `TokenHash` : présenter ce jeton aurait rendu DEUX sessions, et la
        // rotation n'en aurait révoqué qu'une — l'autre survivant à la déconnexion.
        //
        // CE N'EST PAS UN RISQUE DE COLLISION, ET C'EST POUR ÇA QUE C'EST SÛR.
        //
        // Le jeton vient de `RandomNumberGenerator.GetBytes(32)`, haché en SHA-256 :
        // deux tirages identiques n'arriveront pas. Cette contrainte ne se pose donc
        // pas contre le hasard, elle se pose contre un BUG — une régression du
        // générateur, une insertion rejouée, une reprise de données maladroite. Ce
        // sont exactement les cas où l'on veut que la base refuse au lieu de créer
        // une session fantôme.
        //
        // Et c'est aussi pourquoi la migration qui l'accompagne ne court aucun
        // risque de doublons existants : s'il y en avait, ce serait déjà l'incident.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex("UserId");
    }
}
