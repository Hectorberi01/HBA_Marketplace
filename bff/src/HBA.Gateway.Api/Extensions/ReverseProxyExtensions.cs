using HBA.Gateway.Infrastructure.Authentication;
using HBA.Gateway.Infrastructure.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace HBA.Gateway.Api.Extensions;

public static class ReverseProxyExtensions
{
    public static IServiceCollection AddGatewayReverseProxy(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))

            // Les adresses viennent de la section `Services`, pas des clusters.
            .AddConfigFilter<ServiceAddressConfigFilter>()

            .AddTransforms(context =>
            {
                // EFFACER LES EN-TÊTES QUE LES SERVICES POURRAIENT CROIRE INTERNES
                // — AVANT DE TRANSMETTRE.
                foreach (var header in OutboundHeaderPolicy.StrippedFromInbound)
                {
                    context.AddRequestHeaderRemove(header);
                }

                // Identifie l'origine côté service, pour distinguer dans ses
                // journaux un appel passé par la passerelle d'un appel direct entre
                // services sur le réseau interne.
                context.AddRequestHeader("X-Forwarded-By", "hba-gateway", append: false);
            });

        return services;
    }
}
