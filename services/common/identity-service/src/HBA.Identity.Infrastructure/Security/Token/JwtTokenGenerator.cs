using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using HBA.Identity.Application.Abstractions;
using HBA.Identity.Domain.Users;

namespace HBA.Identity.Infrastructure.Security;

/// <summary>
/// Émet un JWT signé (HMAC-SHA256) portant l'identité, les rôles et les
/// permissions. Le « security_stamp » permet d'invalider les tokens après un
/// changement sensible (mot de passe, MFA).
/// </summary>
internal sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    public const string PermissionClaimType = "permission";
    public const string SecurityStampClaimType = "security_stamp";

    private readonly JwtOptions _options;

    public JwtTokenGenerator(JwtOptions options)
        => _options = options;

    /// <summary>Claim OIDC : instant de l'authentification effective, en secondes epoch.</summary>
    public const string AuthTimeClaimType = "auth_time";

    /// <summary>Claim OIDC (RFC 8176) : méthodes d'authentification employées.</summary>
    public const string AuthMethodsClaimType = "amr";

    public AccessToken Generate(
        User user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        AuthenticationSnapshot session)
    {
        var expiresOnUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email.Value),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(SecurityStampClaimType, user.SecurityStamp.ToString()),

            // ═════════════════════════════════════════════════════════════════
            // `auth_time` EST DISTINCT DE `iat`, ET C'EST TOUT LE MÉCANISME.
            //
            // `iat` naît avec ce jeton-ci ; `auth_time` remonte à la connexion qui
            // l'autorise, et traverse les rotations sans bouger. Les dériver du
            // même instant rendrait le step-up du §37 décoratif : un client qui
            // rafraîchit toutes les quatre minutes paraîtrait indéfiniment
            // fraîchement authentifié, et l'exigence de mot de passe récent avant
            // un virement se contournerait en attendant.
            //
            // TYPE `Integer64` EXPLICITE.
            //
            // Sans lui, le sérialiseur écrit la valeur en CHAÎNE — `"1755600000"`
            // au lieu de `1755600000`. C'est conforme au JWT mais pas à OIDC, qui
            // impose un NumericDate, et les bibliothèques clientes qui typent le
            // claim en nombre lèvent au lieu de lire.
            // ═════════════════════════════════════════════════════════════════
            new(
                AuthTimeClaimType,
                new DateTimeOffset(
                    DateTime.SpecifyKind(session.AuthenticatedAtUtc, DateTimeKind.Utc))
                    .ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        // UN CLAIM PAR MÉTHODE : `amr` est un TABLEAU dans OIDC. Concaténées
        // dans une seule valeur, `"pwd otp"` serait lu comme une méthode unique
        // de ce nom, qui n'existe dans aucun registre.
        claims.AddRange(session.MethodList().Select(m => new Claim(AuthMethodsClaimType, m)));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(PermissionClaimType, permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresOnUtc,
            signingCredentials: credentials);

        var serialized = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(serialized, expiresOnUtc);
    }
}
