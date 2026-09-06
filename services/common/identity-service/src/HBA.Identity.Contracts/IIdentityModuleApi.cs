namespace HBA.Identity.Contracts;

/// <summary>
/// API in-process publique du module Identity. Seul moyen pour un autre module
/// (Sellers, Notifications…) de lire un compte de façon synchrone — jamais en
/// touchant la base ou les entités internes.
/// </summary>
/// <summary>Raisons d'un refus de validation. Chaînes stables : elles traversent gRPC.</summary>
public static class TokenRejectionReasons
{
    public const string Expired = "EXPIRED";
    public const string SignatureInvalid = "SIGNATURE_INVALID";
    public const string UserUnknown = "USER_UNKNOWN";
    public const string UserSuspended = "USER_SUSPENDED";
}

/// <summary>
/// Résultat d'une validation de jeton. `Reason` n'est renseigné que si `Valid`
/// est faux — un appelant ne doit pas avoir à deviner pourquoi il a été refusé.
/// </summary>
public sealed record AccessTokenValidation(
    bool Valid,
    Guid UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    string? Reason);

/// <summary>Droits effectifs d'un compte.</summary>
public sealed record UserAuthorization(
    Guid UserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public interface IIdentityModuleApi
{
    Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide un jeton d'accès ET l'état du compte (§10.1).
    ///
    /// À N'APPELER QUE SUR LES CHEMINS SENSIBLES.
    ///
    /// La validation locale du JWT reste le chemin normal : appeler ceci sur chaque
    /// requête ferait d'identity-service un point de panne unique pour toute la
    /// plateforme. L'appel se justifie là où quinze minutes de sursis sont de trop —
    /// un paiement, un retrait, une action d'administration.
    /// </summary>
    Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Rôles et permissions effectifs d'un compte (§10.1).</summary>
    Task<UserAuthorization?> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Révoque toutes les sessions d'un compte et rend le nombre de jetons révoqués (§10.1).
    /// Zéro est un succès : aucune session n'était ouverte.
    /// </summary>
    Task<int> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
