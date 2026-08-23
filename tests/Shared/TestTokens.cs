using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HBA.Shared.Hosting.Http;
using Microsoft.IdentityModel.Tokens;

namespace HBA.Tests.Authorization;

/// <summary>Fabrique des jetons HS256 conformes à ce qu'émet identity-service.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA FORME DU JETON EST CALQUÉE SUR CELLE DU VRAI ÉMETTEUR.
///
/// Rôles écrits sous <see cref="ClaimTypes.Role"/> — donc l'URI longue dans le
/// jeton — et identifiant sous `sub`, parce que le socle pose
/// `MapInboundClaims = false`, `NameClaimType = "sub"` et
/// `RoleClaimType = ClaimTypes.Role` (voir ServiceHostExtensions). Un jeton de
/// test plus simple ne prouverait rien : il validerait les services contre un
/// format que personne n'émet.
///
/// CE FICHIER EST LIÉ, PAS COPIÉ, dans les trois projets de test
/// (`&lt;Compile Include="..\Shared\TestTokens.cs" /&gt;`). Trois copies auraient
/// divergé, et le jour où le socle change de nom de claim, deux suites sur trois
/// passeraient encore.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

    /// <summary>
    /// Le même compte, mais authentifié il y a longtemps.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// EXISTE POUR QUE LE STEP-UP RESTE ÉPROUVÉ, ET NON POUR LE CONTOURNER.
    ///
    /// `Create` pose un `auth_time` frais. Sans cette variante, TOUTE la suite
    /// franchirait le contrôle du §37 et plus aucun test ne prouverait qu'il mord.
    /// On aurait ajouté un claim pour faire passer des tests — c'est-à-dire
    /// désactivé une garde de sécurité, en silence, dans tout le dépôt.
    ///
    /// Un jeton fabriqué ici doit se voir refuser `PUT /payout-account` et
    /// `POST /close` en 403 `reauthentication.required:…`, et passer partout
    /// ailleurs.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static string CreateAuthentificationAncienne(Guid userId, params string[] roles)
        => Create(userId, DateTimeOffset.UtcNow - StepUpAuthentication.Window - TimeSpan.FromMinutes(1), roles);

    /// <summary>
    /// La fabrique complète : c'est <c>authentifieLe</c> qui décide du step-up.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `auth_time` MANQUAIT, ET SON ABSENCE RENDAIT 403 SUR DIX TESTS.
    ///
    /// Le §37 impose une authentification récente sur les gestes qui détournent de
    /// l'argent — `PAYOUT_CONFIGURE`, `SELLER_CLOSE`. `HasRecentAuthentication`
    /// REFUSE quand le claim est absent, et c'est le bon choix : traiter
    /// « inconnu » comme « accepté » ouvrirait le contournement le plus simple qui
    /// soit, présenter un vieux jeton.
    ///
    /// Un jeton de test sans `auth_time` n'est donc pas un jeton simplifié : c'est
    /// un jeton que personne ne peut obtenir en se connectant. Il fait échouer les
    /// parcours pour une raison qui n'existe pas en production.
    ///
    /// `auth_time` N'EST PAS `iat`, ET LA DISTINCTION EST TOUT LE MÉCANISME.
    ///
    /// Un jeton rafraîchi porte un `iat` neuf — c'est sa raison d'être — et
    /// recopie l'`auth_time` de la session d'origine. Les confondre rendrait un
    /// client qui rafraîchit toutes les quatre minutes éternellement « fraîchement
    /// authentifié ». D'où un paramètre distinct de l'émission.
    ///
    /// `amr` accompagne : RFC 8176, la méthode employée. `pwd` est ce qu'émet
    /// identity-service après une connexion par mot de passe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
