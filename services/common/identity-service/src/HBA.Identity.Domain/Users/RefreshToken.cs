using HBA.Shared.Domain.Primitives;

namespace HBA.Identity.Domain.Users;

/// <summary>
/// Jeton de rafraîchissement. On ne stocke jamais le token en clair : seul son
/// hash est persisté. Entité enfant de l'agrégat User.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
    {
    }

    internal RefreshToken(
        Guid id, string tokenHash, DateTime expiresOnUtc,
        DateTime authenticatedAtUtc, string authMethods)
        : base(id)
    {
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = DateTime.UtcNow;
        AuthenticatedAtUtc = authenticatedAtUtc;
        AuthMethods = authMethods;
    }

    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? RevokedOnUtc { get; private set; }

    /// <summary>
    /// L'instant où le titulaire a RÉELLEMENT prouvé son identité — pas celui où
    /// ce jeton a été créé.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST LA COLONNE QUI EMPÊCHE DE CONTOURNER LE STEP-UP (§37).
    ///
    /// `CreatedOnUtc` est neuf à chaque rotation — c'est sa raison d'être. Si le
    /// claim `auth_time` en découlait, un client qui rafraîchit toutes les quatre
    /// minutes resterait indéfiniment « fraîchement authentifié », et l'exigence
    /// de mot de passe récent sur un virement ne coûterait rien à contourner :
    /// il suffirait d'attendre.
    ///
    /// Cette valeur, elle, TRAVERSE les rotations sans bouger. Elle ne change
    /// qu'à une nouvelle connexion, ou à une réauthentification explicite.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public DateTime AuthenticatedAtUtc { get; private set; }

    /// <summary>
    /// Les méthodes employées à cette authentification, séparées par des espaces —
    /// `pwd`, `pwd otp`. C'est la forme du claim `amr` (RFC 8176).
    /// </summary>
    /// <remarks>
    /// UNE CHAÎNE ET NON UNE TABLE : la valeur est écrite une fois, lue une
    /// fois, jamais filtrée ni jointe. Une table d'association coûterait une
    /// jointure par rafraîchissement pour deux mots.
    /// </remarks>
    public string AuthMethods { get; private set; } = default!;

    public bool IsActive(DateTime nowUtc) => RevokedOnUtc is null && ExpiresOnUtc > nowUtc;

    internal void Revoke() => RevokedOnUtc = DateTime.UtcNow;
}
