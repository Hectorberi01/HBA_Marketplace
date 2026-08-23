using HBA.Identity.Application.Abstractions;
using HBA.Identity.Application.Models;
using HBA.Identity.Domain.Roles;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Application.Users;

/// <summary>
/// Émet une paire de jetons pour un utilisateur : résout ses rôles et
/// permissions (pour les claims JWT), génère l'access token et un refresh token
/// (dont seul le hash est stocké sur l'agrégat). Ne persiste pas — l'appelant
/// committe via l'Unit of Work.
/// </summary>
internal sealed class AuthTokenIssuer
{
    private readonly IRoleRepository _roleRepository;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;

    public AuthTokenIssuer(
        IRoleRepository roleRepository,
        IJwtTokenGenerator jwtTokenGenerator,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings)
    {
        _roleRepository = roleRepository;
        _jwtTokenGenerator = jwtTokenGenerator;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
    }

    /// <param name="session">
    /// Quand et comment le titulaire s'est authentifié.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// PARAMÈTRE OBLIGATOIRE, SANS VALEUR PAR DÉFAUT, ET C'EST DÉLIBÉRÉ.
    ///
    /// Un défaut à `AuthenticationSnapshot.ByPassword(DateTime.UtcNow)` aurait
    /// évité de toucher les deux appelants — et aurait fait exactement ce qu'il ne
    /// faut pas au rafraîchissement : rajeunir `auth_time` à chaque rotation, ce
    /// qui vide le step-up du §37. Le compilateur oblige donc chaque appelant à
    /// dire lequel des deux cas il est.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </param>
    public async Task<AuthTokens> IssueAsync(
        User user, AuthenticationSnapshot session, CancellationToken cancellationToken)
    {
        var roleIds = user.RoleIds.Select(id => new RoleId(id)).ToList();
        var roles = await _roleRepository.GetByIdsAsync(roleIds, cancellationToken);

        var roleNames = roles.Select(r => r.Name).ToList();
        var permissions = roles.SelectMany(r => r.Permissions).Distinct().ToList();

        var accessToken = _jwtTokenGenerator.Generate(user, roleNames, permissions, session);

        var (rawRefresh, refreshHash) = _tokenGenerator.Generate();
        var refreshExpiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.RefreshTokenLifetime);
        user.IssueRefreshToken(refreshHash, refreshExpiresOnUtc, session.AuthenticatedAtUtc, session.Methods);

        return new AuthTokens(accessToken.Token, accessToken.ExpiresOnUtc, rawRefresh, refreshExpiresOnUtc);
    }
}
