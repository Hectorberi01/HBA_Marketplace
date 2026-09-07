using HBA.Gateway.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.Infrastructure;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka;

/// <summary>
/// LA PASSERELLE CONSOMME DU KAFKA — UN SEUL EVENEMENT, POUR UNE PROPRIETE DE
/// SECURITE.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterMessagerieGateway(
        this IServiceCollection services, IConfigurationManager configuration)
    {
        // POSEE AVANT `AddBuildingBlocksInfrastructure`, QUI LIT LA VALEUR.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:ConsumerGroup"] = $"hba-gateway-{Environment.MachineName}"
        });

        services.AddBuildingBlocksInfrastructure(configuration);
        services.AjouterSujetsGateway();

        // Singleton : le registre porte l'etat qui relie le middleware — lui aussi
        // unique pour le processus — au gestionnaire, resolu par message.
        services.AddSingleton<RegistreDeRevocation>();

        services.AddScoped<
            IIntegrationEventHandler<TokenRevokedIntegrationEvent>,
            InvaliderLeCacheSurRevocationHandler>();

        return services;
    }
}
