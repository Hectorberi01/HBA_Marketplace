using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Limitation de débit interne à un service.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// OUI, LA PASSERELLE LIMITE DÉJÀ. CE LIMITEUR-CI RESTE NÉCESSAIRE.
///
/// La passerelle applique ses politiques `auth` et `otp` au trafic qui passe par
/// elle. Sur le réseau `hba-backend`, tout service peut atteindre identity-service
/// DIRECTEMENT — un autre conteneur, un job de maintenance, une configuration
/// Docker erronée. Ce trafic-là ne traverse aucune politique de passerelle.
///
/// Le retirer laisserait `/api/identity/auth/login` sans protection contre
/// l'énumération dès qu'on l'atteint autrement que par la porte d'entrée. C'est
/// la même raison qui fait revalider le jeton dans chaque service.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// VERSION RÉDUITE PAR RAPPORT AU MONOLITHE. LA COUPE EST DÉLIBÉRÉE.
///
/// L'original portait une troisième politique, pour les partenaires de l'API
/// Delivery, qui appelait `PartnerApiKeyFilter.HeaderName` et
/// `HBA.Deliveries.Domain.Partners.PartnerApiKey.ExtractPrefix`. Le copier ici
/// aurait fait entrer le module Delivery — non extrait — dans le socle partagé
/// par les treize services : media-service aurait dépendu du domaine des courses.
///
/// Cette politique appartient à delivery-service et devra y être recréée au
/// moment de son extraction, avec son plafond par adresse — ce plafond n'était
/// pas décoratif : un préfixe de clé s'invente, et sans lui il suffisait de
/// générer des clés bien formées pour obtenir un seau neuf à chaque requête.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class AuthRateLimiter
{
    /// <summary>Politique stricte : connexion, inscription, réinitialisation.</summary>
    public const string PolicyName = "auth";

    /// <summary>Politique générale, appliquée à tout le reste.</summary>
    public const string GlobalPolicyName = "global";

    // 30/min et non 10 : derrière un CGNAT — la norme au Bénin — dix personnes se
    // connectant dans la même minute ne sont pas une attaque, c'est une heure de
    // pointe. Un attaquant réel envoie des milliers de requêtes : 30 le freine
    // autant que 10, sans punir les utilisateurs légitimes.
    private const int AuthPermitPerWindow = 30;
    private static readonly TimeSpan AuthWindow = TimeSpan.FromMinutes(1);

    // Un utilisateur qui navigue fait 30 à 60 appels/minute. 300 laisse de la
    // marge et arrête un robot qui aspirerait le catalogue.
    private const int GlobalPermitPerWindow = 300;
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddAuthRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Endpoints d'authentification : anonymes par nature, donc
            // partitionnables uniquement par adresse.
            options.AddPolicy(PolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth:{ClientIp(httpContext)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = AuthPermitPerWindow,
                        Window = AuthWindow,
                        QueueLimit = 0
                    }));

            options.AddPolicy(GlobalPolicyName, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitPerWindow,
                        Window = GlobalWindow,
                        QueueLimit = 0
                    }));

            // Filet appliqué à TOUT endpoint qui ne déclare pas sa propre
            // politique — sans quoi seules les routes explicitement décorées
            // seraient protégées, et l'oubli passerait inaperçu.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitPerWindow,
                        Window = GlobalWindow,
                        QueueLimit = 0
                    }));
        });

        return services;
    }

    /// <summary>
    /// L'identifiant de compte dès qu'il existe, l'adresse sinon.
    /// </summary>
    /// <remarks>
    /// Partitionner par adresse seule neutraliserait la protection derrière un
    /// CGNAT : deux clients du même opérateur se voleraient leur quota. Le claim
    /// `sub` est signé, donc infalsifiable, et suit l'utilisateur qui change de
    /// réseau en pleine commande.
    /// </remarks>
    private static string PartitionKey(HttpContext httpContext)
    {
        var subject = httpContext.User.FindFirstValue("sub")
                      ?? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        return string.IsNullOrWhiteSpace(subject)
            ? $"ip:{ClientIp(httpContext)}"
            : $"sub:{subject}";
    }

    private static string ClientIp(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
