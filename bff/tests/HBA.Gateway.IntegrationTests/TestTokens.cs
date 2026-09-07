using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace HBA.Gateway.IntegrationTests;

/// <summary>Fabrique des jetons HS256 conformes à ce qu'émet identity-service.</summary>
internal static class TestTokens
{
    /// <summary>36 octets : au-dessus du minimum de 32 imposé par la validation.</summary>
    public const string SigningKey = "cle-de-test-hba-gateway-32-octets-ok";

    public const string Issuer = "hba-identity";
    public const string Audience = "hba-platform";

    /// <summary>
    /// LA FORME DU JETON EST CALQUÉE SUR `JwtTokenGenerator` DU MONOLITHE.
    ///
    /// Rôles écrits sous <see cref="ClaimTypes.Role"/> — donc l'URI longue dans
    /// le jeton — et identifiant sous `sub`. Un test qui fabriquerait un jeton
    /// plus simple ne prouverait rien : il validerait la passerelle contre un
    /// format que le vrai émetteur ne produit pas.
    /// </summary>
    public static string Create(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
