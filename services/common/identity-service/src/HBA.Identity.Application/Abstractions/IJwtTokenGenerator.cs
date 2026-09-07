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
    /// <param name="session">Quand et comment le titulaire s'est authentifié.</param>
    AccessToken Generate(
        User user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        AuthenticationSnapshot session);
}
