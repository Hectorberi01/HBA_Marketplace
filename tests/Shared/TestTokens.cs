using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HBA.Shared.Hosting.Http;
using Microsoft.IdentityModel.Tokens;

namespace HBA.Tests.Authorization;

/// <summary>Fabrique des jetons HS256 conformes à ce qu'émet identity-service.</summary>
public static class TestTokens
{
    /// <summary>36 octets : au-dessus du minimum de 32 imposé par la validation.</summary>
    public const string SigningKey = "cle-de-test-hba-services-32-octets-!";

    public const string Issuer = "hba-identity";
    public const string Audience = "hba-platform";

    /// <summary>Un compte quelconque, avec les rôles demandés.</summary>
    public static string Create(params string[] roles) => Create(Guid.NewGuid(), roles);

    /// <summary>Un compte DÉSIGNÉ, pour éprouver les contrôles de propriété.</summary>
    public static string Create(Guid userId, params string[] roles)
        => Create(userId, DateTimeOffset.UtcNow, roles);

    /// <summary>Le même compte, mais authentifié il y a longtemps.</summary>
    public static string CreateAuthentificationAncienne(Guid userId, params string[] roles)
        => Create(userId, DateTimeOffset.UtcNow - StepUpAuthentication.Window - TimeSpan.FromMinutes(1), roles);

    /// <summary>La fabrique complète : c'est <c>authentifieLe</c> qui décide du step-up.</summary>
    public static string Create(Guid userId, DateTimeOffset authentifieLe, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(StepUpAuthentication.AuthTimeClaim, authentifieLe.ToUnixTimeSeconds().ToString()),
            new(StepUpAuthentication.AuthMethodsClaim, "pwd")
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
