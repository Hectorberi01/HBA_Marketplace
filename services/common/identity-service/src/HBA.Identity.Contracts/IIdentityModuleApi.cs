namespace HBA.Identity.Contracts;

/// <summary>API in-process publique du module Identity.</summary>
/// <summary>Raisons d'un refus de validation.</summary>
public static class TokenRejectionReasons
{
    public const string Expired = "EXPIRED";
    public const string SignatureInvalid = "SIGNATURE_INVALID";
    public const string UserUnknown = "USER_UNKNOWN";
    public const string UserSuspended = "USER_SUSPENDED";
}

/// <summary>Résultat d'une validation de jeton.</summary>
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

    /// <summary>Valide un jeton d'accès ET l'état du compte (§10.1).</summary>
    Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Rôles et permissions effectifs d'un compte (§10.1).</summary>
    Task<UserAuthorization?> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Révoque toutes les sessions d'un compte et rend le nombre de jetons révoqués
    /// (§10.1).
    /// </summary>
    Task<int> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
