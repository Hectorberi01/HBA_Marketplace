using HBA.Identity.Contracts;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE VOISIN QU'ON NE FAIT PAS TOURNER — ET RIEN D'AUTRE.
///
/// `RegisterSellerCommandHandler` demande à Identity si le compte existe et si son
/// e-mail est confirmé, AVANT d'inscrire quoi que ce soit. Cette classe répond
/// « oui » pour tout identifiant : c'est ce qui permet aux tests de partir d'un
/// compte quelconque sans démarrer un second service.
///
/// RÉPONDRE « OUI » À TOUT EST UN CHOIX, PAS UNE FACILITÉ.
///
/// Les deux refus — compte inconnu, e-mail non confirmé — sont des règles de
/// seller-service, pas d'Identity : elles vivent dans le handler, et c'est
/// `HBA.Merchants.UnitTests` qui doit les tenir. Les rejouer ici demanderait de
/// piloter la fausse identité depuis chaque test, ce qui reviendrait à éprouver la
/// fausse identité elle-même.
///
/// Ce qu'on veut de ce niveau est ailleurs : les migrations, l'outbox, le
/// courtier, l'inbox. Tout cela est réel.
///
/// LES QUATRE AUTRES MEMBRES LÈVENT, ILS NE RENDENT PAS UNE VALEUR VIDE.
///
/// seller-service n'appelle qu'un seul membre de ce contrat. Si un futur chemin de
/// code se met à en appeler un autre, il doit s'en apercevoir ici — une valeur
/// neutre rendue en silence ferait passer un test qui n'éprouve plus ce qu'il
/// annonce, et c'est exactement le mode de panne que cette suite existe pour
/// attraper.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class IdentiteDeTest : IIdentityModuleApi
{
    public Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<UserSummary?>(new UserSummary(
            Id: userId,
            FirstName: "Kossi",
            LastName: "Adjovi",
            Email: $"{userId:N}@exemple.bj",
            PhoneNumber: "+22997000000",
            Status: "Active",
            EmailVerified: true,
            MfaEnabled: false,
            RoleIds: Array.Empty<Guid>()));

    public Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "seller-service ne lit pas un compte par e-mail. Si c'est devenu le cas, "
            + "ce test doit décider quoi répondre — pas hériter d'un défaut silencieux.");

    public Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "seller-service valide ses jetons localement. Un appel ici signalerait un "
            + "changement de chemin d'authentification qui mérite d'être vu.");

    public Task<UserAuthorization?> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Les rôles viennent du jeton dans cette suite. Voir `TestTokens`.");

    public Task<int> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "seller-service ne révoque pas de session.");
}
