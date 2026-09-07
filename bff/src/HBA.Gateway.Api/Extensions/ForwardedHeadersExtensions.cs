using System.Net;
using HBA.Gateway.Api.Options;
using Microsoft.AspNetCore.HttpOverrides;

namespace HBA.Gateway.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Configure la prise en compte des en-têtes <c>X-Forwarded-*</c> posés par
    /// Traefik.
    /// </summary>
    public static IServiceCollection AddGatewayForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration, ILogger logger)
    {
        var trust = configuration
            .GetSection(ProxyTrustOptions.SectionName)
            .Get<ProxyTrustOptions>() ?? new ProxyTrustOptions();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            if (trust.TrustAnyProxy)
            {
                // Vider les deux listes DÉSACTIVE le contrôle d'origine : c'est
                // précisément ce que fait « faire confiance à tout le monde ».
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();

                logger.LogWarning(
                    "ProxyTrust:TrustAnyProxy est actif : les en-têtes X-Forwarded-* sont "
                    + "acceptés de n'importe quelle source. La limitation de débit du trafic "
                    + "anonyme devient contournable si la passerelle est joignable hors de Traefik.");

                return;
            }

            foreach (var candidate in trust.KnownProxies)
            {
                if (IPAddress.TryParse(candidate, out var address))
                {
                    options.KnownProxies.Add(address);
                    continue;
                }

                // ON NE LÈVE PAS, MAIS ON NE SE TAIT PAS NON PLUS.
                //
                // Une adresse mal saisie ne doit pas empêcher le démarrage — les
                // autres restent valides. Mais ignorée en silence, elle produirait
                // une passerelle qui semble configurée et qui, en réalité, ne fait
                // confiance à rien : toutes les IP clientes deviendraient celle de
                // Traefik, sans qu'aucun symptôme ne le signale.
                logger.LogError(
                    "ProxyTrust:KnownProxies contient une adresse IP invalide, ignorée : {Value}",
                    candidate);
            }

            if (options.KnownProxies.Count == 0)
            {
                logger.LogWarning(
                    "Aucun proxy de confiance déclaré : X-Forwarded-For sera ignoré et l'IP "
                    + "vue sera celle de Traefik. La limitation de débit anonyme sera donc "
                    + "commune à tous les clients. Renseigner ProxyTrust:KnownProxies.");
            }
        });

        return services;
    }
}
