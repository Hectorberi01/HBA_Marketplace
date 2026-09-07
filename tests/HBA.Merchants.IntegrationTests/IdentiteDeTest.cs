using HBA.Identity.Contracts;

namespace HBA.Merchants.IntegrationTests;

/// <summary>LE VOISIN QU'ON NE FAIT PAS TOURNER — ET RIEN D'AUTRE.</summary>
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
