using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Infrastructure.Messaging.Kafka.Configuration;
using HBA.Users.Infrastructure.Messaging.Kafka.Consumers;
using HBA.Users.Infrastructure.Messaging.Kafka.Inbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Outbox;
using HBA.Users.Infrastructure.Messaging.Kafka.Producers;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Users.Infrastructure.Messaging.Kafka;

/// <summary>LE MODULE KAFKA DE user-service — UN SEUL POINT D'ENTRÉE.</summary>
public static class DependencyInjection
{
    /// <summary>Branche toute la messagerie du service.</summary>
    public static IServiceCollection AjouterMessagerieUsers(this IServiceCollection services)
    {
        // CE QU'ON PUBLIE EST VÉRIFIÉ AVANT CE QU'ON ÉCOUTE.
        EvenementsPublies.VerifierLesDescripteurs();

        services.AjouterSujetsUsers();
        services.AjouterOutboxUsers();
        services.AjouterInboxUsers();

        // Inscription → création du profil.
        services.AddScoped<
            IIntegrationEventHandler<UserRegisteredIntegrationEvent>,
            CreateUserProfileOnUserRegisteredHandler>();

        // Le compte change de nom → le profil suit.
        services.AddScoped<
            IIntegrationEventHandler<UserProfileUpdatedIntegrationEvent>,
            RenameUserProfileOnIdentityProfileUpdatedHandler>();

        // CELUI-CI EST UNE OBLIGATION LÉGALE, PAS UN CONFORT.
        services.AddScoped<
            IIntegrationEventHandler<UserAnonymizedIntegrationEvent>,
            PurgeUserDataOnAccountAnonymizedHandler>();

        return services;
    }
}
