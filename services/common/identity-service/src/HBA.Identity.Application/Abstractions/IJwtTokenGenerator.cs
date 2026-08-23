using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Abstractions;

/// <summary>Jeton d'accès JWT émis et sa date d'expiration (UTC).</summary>
public sealed record AccessToken(string Token, DateTime ExpiresOnUtc);

/// <summary>
/// Génère un JWT signé portant l'identité, les rôles et les permissions de
/// l'utilisateur (implémenté en Infrastructure).
/// </summary>
public interface IJwtTokenGenerator
{
    /// <param name="session">
    /// Quand et comment le titulaire s'est authentifié. Devient les claims OIDC
    /// `auth_time` et `amr`, sur lesquels repose le step-up du §37.
    ///
    /// CE N'EST PAS L'INSTANT D'ÉMISSION. Voir <see cref="AuthenticationSnapshot"/> :
    /// le confondre avec `iat` rendrait le step-up contournable par simple attente.
    /// </param>
    AccessToken Generate(
        User user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        AuthenticationSnapshot session);
}
