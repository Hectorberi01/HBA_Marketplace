using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.Authorization;

namespace HBA.Gateway.Api.Extensions;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Déclare les politiques d'autorisation à partir de la section
    /// <c>Authorization:Roles</c>.
    /// </summary>
    public static IServiceCollection AddGatewayAuthorization(
        this IServiceCollection services, IConfiguration configuration)
    {
        var mapping = configuration
            .GetSection(GatewayAuthorizationOptions.SectionName)
            .Get<GatewayAuthorizationOptions>() ?? new GatewayAuthorizationOptions();

        var authenticated = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        var builder = services.AddAuthorizationBuilder()
            .SetDefaultPolicy(authenticated)

            // ═════════════════════════════════════════════════════════════════
            // POLITIQUE DE REPLI : AUTHENTIFICATION EXIGÉE PAR DÉFAUT.
            //
            // Elle s'applique à tout point de terminaison qui ne déclare RIEN.
            // Le sens de l'oubli en dépend entièrement :
            //   • sans repli, une route ajoutée sans mention est PUBLIQUE ;
            //   • avec ce repli, elle est fermée.
            //
            // Une route de commandes ou de portefeuille rendue publique par
            // distraction ne produit aucune erreur — elle fonctionne, simplement
            // pour tout le monde. Ce défaut-là se découvre par un rapport externe.
            // L'inverse se découvre en trente secondes, parce que l'application
            // cliente reçoit un 401.
            //
            // Les routes réellement publiques portent `"AuthorizationPolicy":
            // "anonymous"` dans la configuration YARP, et les sondes de santé
            // `AllowAnonymous`.
            // ═════════════════════════════════════════════════════════════════
            .SetFallbackPolicy(authenticated)

            .AddPolicy(GatewayPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());

        foreach (var policyName in GatewayPolicies.RoleBased)
        {
            var roles = mapping.Roles.GetValueOrDefault(policyName) ?? [];

            builder.AddPolicy(policyName, policy =>
            {
                policy.RequireAuthenticatedUser();

                if (roles.Length > 0)
                {
                    policy.RequireRole(roles);
                    return;
                }

                // UNE POLITIQUE SANS RÔLE CONFIGURÉ NE LAISSE PASSER PERSONNE.
                //
                // `RequireRole()` avec un tableau vide n'ajoute AUCUNE exigence :
                // la politique se réduirait à « être authentifié », et `AdminOnly`
                // ouvrirait l'administration à n'importe quel compte client.
                // Refuser tout le monde rend l'erreur de configuration
                // immédiatement visible, et du bon côté.
                policy.RequireAssertion(_ => false);
            });
        }

        return services;
    }
}
