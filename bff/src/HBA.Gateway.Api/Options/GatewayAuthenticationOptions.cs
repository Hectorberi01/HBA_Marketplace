using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Api.Options;

/// <summary>Validation des jetons d'accès émis par identity-service.</summary>
public sealed class GatewayAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Émetteur attendu. Correspond à <c>Jwt__Issuer</c> d'identity-service.</summary>
    [Required] public string Issuer { get; init; } = string.Empty;

    /// <summary>Audience attendue. Correspond à <c>Jwt__Audience</c> d'identity-service.</summary>
    [Required] public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Clé symétrique partagée (HS256).
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE CLÉ NE VIENT QUE D'UNE VARIABLE D'ENVIRONNEMENT OU D'UN COFFRE.
    ///    JAMAIS D'UN appsettings VERSIONNÉ.
    ///
    /// En HMAC, la clé qui VÉRIFIE est la même que celle qui SIGNE. Quiconque la
    /// lit ne se contente pas de lire les jetons : il en forge, avec le rôle
    /// « Admin » et l'identifiant de n'importe quel utilisateur. C'est la
    /// différence de nature avec une clé publique RS256, qu'on peut publier sans
    /// risque.
    ///
    /// Elle est ici parce que identity-service signe aujourd'hui en HS256
    /// (`JwtTokenGenerator` du monolithe : `SymmetricSecurityKey` +
    /// `HmacSha256`). Le jour où il passera en RS256, laisser ce champ vide et
    /// renseigner `Authority` suffit — aucun code à modifier.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public string? SigningKey { get; init; }

    /// <summary>
    /// Autorité OIDC, utilisée UNIQUEMENT si <see cref="SigningKey"/> est vide.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>Récupération des métadonnées OIDC en HTTPS. Ne passer à <c>false</c> qu'en développement.</summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// Type de claim portant les rôles.
    /// </summary>
    /// <remarks>
    /// Valeur par défaut relevée dans le code d'émission existant : le monolithe
    /// écrit <c>new Claim(ClaimTypes.Role, role)</c>, ce qui produit dans le jeton
    /// le nom long <c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>.
    /// Configurable parce que ce choix peut évoluer lors de l'extraction du service.
    /// </remarks>
    public string RoleClaimType { get; init; } = System.Security.Claims.ClaimTypes.Role;

    /// <summary>
    /// Tolérance d'horloge. La valeur .NET par défaut est de CINQ MINUTES, ce qui
    /// prolonge d'autant la validité d'un jeton révoqué ou expiré.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "00:05:00")]
    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Refuse au démarrage une configuration d'authentification inexploitable.
/// </summary>
public sealed class GatewayAuthenticationOptionsValidator : IValidateOptions<GatewayAuthenticationOptions>
{
    /// <summary>Taille minimale d'une clé HMAC-SHA256 : 256 bits.</summary>
    private const int MinimumKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, GatewayAuthenticationOptions options)
    {
        var hasKey = !string.IsNullOrWhiteSpace(options.SigningKey);
        var hasAuthority = !string.IsNullOrWhiteSpace(options.Authority);

        if (!hasKey && !hasAuthority)
        {
            // SANS CE GARDE, LE DÉFAUT EST « TOUT JETON EST REFUSÉ », EN SILENCE.
            //
            // `AddJwtBearer` sans clé ni autorité démarre sans broncher, puis rend
            // 401 sur chaque requête authentifiée. Rien dans les journaux ne dit
            // que la CONFIGURATION est en cause : on cherche du côté du client,
            // du jeton, de l'horloge, et le vrai motif reste invisible.
            return ValidateOptionsResult.Fail(
                "Authentication : renseigner soit 'SigningKey' (HS256, mode actuel "
                + "d'identity-service), soit 'Authority' (découverte OIDC). "
                + "Sans l'un des deux, aucun jeton ne peut être validé.");
        }

        if (hasKey && Encoding.UTF8.GetByteCount(options.SigningKey!) < MinimumKeyBytes)
        {
            // Une clé HMAC plus courte que l'empreinte qu'elle produit réduit le
            // coût d'une recherche exhaustive — et permet alors de FORGER des
            // jetons, pas seulement d'en lire.
            return ValidateOptionsResult.Fail(
                $"Authentication:SigningKey doit faire au moins {MinimumKeyBytes} octets "
                + "(256 bits) pour HMAC-SHA256.");
        }

        if (hasKey && hasAuthority)
        {
            return ValidateOptionsResult.Fail(
                "Authentication : 'SigningKey' et 'Authority' sont tous deux renseignés. "
                + "Le mode effectif serait ambigu — n'en garder qu'un.");
        }

        return ValidateOptionsResult.Success;
    }
}
