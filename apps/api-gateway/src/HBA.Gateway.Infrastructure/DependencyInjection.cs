using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Infrastructure.Authentication;
using HBA.Gateway.Infrastructure.Configuration;
using HBA.Gateway.Infrastructure.HttpClients;
using HBA.Gateway.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les adresses des services, les quatorze clients typés, leur
    /// résilience et la propagation d'en-têtes.
    /// </summary>
    public static IServiceCollection AddGatewayInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ServicesOptions>()
            .Bind(configuration.GetSection(ServicesOptions.SectionName))
            .ValidateDataAnnotations()
            // `ValidateOnStart` FAIT ÉCHOUER LE DÉMARRAGE, ET C'EST VOULU.
            //
            // Sans lui, la validation ne s'exécute qu'au PREMIER accès aux
            // options — c'est-à-dire à la première requête client. Le conteneur
            // serait passé « healthy », aurait été mis en rotation, et n'aurait
            // révélé l'adresse manquante qu'en rendant des 500 à de vrais
            // utilisateurs. Un déploiement doit échouer avant de prendre du trafic.
            .ValidateOnStart();

        services
            .AddOptions<OutboundOptions>()
            .Bind(configuration.GetSection(OutboundOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Nécessaire à la propagation : la seule dépendance de cette couche à
        // la requête en cours, et elle est explicite.
        services.AddHttpContextAccessor();
        services.AddTransient<OutboundHeaderPropagationHandler>();

        var outbound = configuration
            .GetSection(OutboundOptions.SectionName)
            .Get<OutboundOptions>() ?? new OutboundOptions();

        AddServiceClient<IIdentityClient, HttpClients.Identity.IdentityClient>(services, ServiceKeys.Identity, outbound);
        AddServiceClient<IUserClient, HttpClients.User.UserClient>(services, ServiceKeys.User, outbound);
        AddServiceClient<IMerchantClient, HttpClients.Merchant.MerchantClient>(services, ServiceKeys.Merchant, outbound);
        AddServiceClient<ICatalogClient, HttpClients.Catalog.CatalogClient>(services, ServiceKeys.Catalog, outbound);
        AddServiceClient<IInventoryClient, HttpClients.Inventory.InventoryClient>(services, ServiceKeys.Inventory, outbound);
        AddServiceClient<ICommerceClient, HttpClients.Commerce.CommerceClient>(services, ServiceKeys.Commerce, outbound);
        AddServiceClient<IOrderClient, HttpClients.Order.OrderClient>(services, ServiceKeys.Order, outbound);
        AddServiceClient<IFoodClient, HttpClients.Food.FoodClient>(services, ServiceKeys.Food, outbound);
        AddServiceClient<IDeliveryClient, HttpClients.Delivery.DeliveryClient>(services, ServiceKeys.Delivery, outbound);
        AddServiceClient<IFinancialClient, HttpClients.Financial.FinancialClient>(services, ServiceKeys.Financial, outbound);
        AddServiceClient<IEngagementClient, HttpClients.Engagement.EngagementClient>(services, ServiceKeys.Engagement, outbound);
        AddServiceClient<ICommunicationClient, HttpClients.Communication.CommunicationClient>(services, ServiceKeys.Communication, outbound);
        AddServiceClient<IMediaClient, HttpClients.Media.MediaClient>(services, ServiceKeys.Media, outbound);

        // QUATORZIÈME — VOIR L'ENCADRÉ D'`IDriversClient`.
        //
        // Son adresse et sa clé existaient déjà ; seul le client manquait, donc
        // driver-service était relayable et NON interrogeable. Le compteur de
        // livreurs à vérifier de l'écran d'accueil admin est le premier à en
        // avoir besoin.
        AddServiceClient<IDriversClient, HttpClients.Drivers.DriversClient>(services, ServiceKeys.Drivers, outbound);

        // PORTÉE REQUÊTE, PAS SINGLETON.
        //
        // Le registre reçoit les quatorze clients typés. En singleton, il les
        // capturerait DÉFINITIVEMENT : les instances d'`HttpClient` ne seraient
        // jamais renouvelées, et le pool de connexions cesserait de suivre les
        // changements DNS. Dans Docker, un conteneur redéployé change d'adresse —
        // la passerelle continuerait d'appeler l'ancienne, indéfiniment, en 502.
        services.AddScoped<IServiceClientRegistry, ServiceClientRegistry>();

        return services;
    }

    /// <summary>
    /// Enregistre un client typé, sa résilience, sa propagation d'en-têtes, et
    /// l'expose AUSSI comme <see cref="IServiceClient"/> pour le registre.
    /// </summary>
    private static void AddServiceClient<TInterface, TImplementation>(
        IServiceCollection services, string serviceKey, OutboundOptions outbound)
        where TInterface : class, IServiceClient
        where TImplementation : class, TInterface
    {
        services
            .AddHttpClient<TInterface, TImplementation>(serviceKey, (provider, client) =>
            {
                var addresses = provider.GetRequiredService<IOptions<ServicesOptions>>().Value;

                // `Resolve` ne peut rendre null ici : la clé vient de ServiceKeys
                // et les options sont validées au démarrage.
                client.BaseAddress = new Uri(addresses.Resolve(serviceKey)!, UriKind.Absolute);

                // Le délai est géré par la pile de résilience, PAS ici.
                //
                // `HttpClient.Timeout` lève un `TaskCanceledException` que Polly
                // ne distingue pas d'une annulation par le client : le disjoncteur
                // ne compterait pas ces échecs et ne s'ouvrirait jamais.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<OutboundHeaderPropagationHandler>()
            .AddHbaResilience(outbound);

        // `GetRequiredService`, PAS une seconde instanciation.
        //
        // Réenregistrer l'implémentation en `AddSingleton<IServiceClient, X>()`
        // créerait un DEUXIÈME objet, sans HttpClient configuré ni résilience :
        // le registre BFF aurait alors un client sans adresse de base, et chaque
        // section serait indisponible sans que rien n'explique pourquoi.
        services.AddTransient<IServiceClient>(provider => provider.GetRequiredService<TInterface>());
    }
}
