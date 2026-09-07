using HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Communication.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Communication.Infrastructure.Messaging.Kafka.Producers;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE LA MESSAGERIE INTERNE — LA DERNIERE EXCEPTION FERMEE.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AjouterMessagerieCommunication(this IServiceCollection services)
    {
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsCommunication();
        services.AjouterOutboxCommunication();

        return services;
    }
}
