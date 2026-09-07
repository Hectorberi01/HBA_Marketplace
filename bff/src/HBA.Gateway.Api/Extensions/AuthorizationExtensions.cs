using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.Authorization;

namespace HBA.Gateway.Api.Extensions;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Déclare les politiques d'autorisation à partir de la section <c>
    /// Authorization:Roles</c>.
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

            // POLITIQUE DE REPLI : AUTHENTIFICATION EXIGÉE PAR DÉFAUT.
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
                policy.RequireAssertion(_ => false);
            });
        }

        return services;
    }
}
