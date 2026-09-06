using HBA.Communication.Infrastructure.Persistence;
using HBA.Shared.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Outbox;

/// <summary>
/// L'OUTBOX DE LA MESSAGERIE INTERNE — LE CABLAGE ICI, LE TYPE ET LA TABLE
///     AILLEURS.
///
/// Cet enregistrement etait reste dans `MessagingModuleInstaller`, seule
/// exception a la regle des vingt-quatre autres services. La raison invoquee
/// tenait : le module ne consomme rien, un dossier complet pour une ligne utile.
/// Elle ne tenait plus des lors que quelqu'un cherchant le videur d'outbox de ce
/// module ne le trouvait pas la ou les vingt-quatre autres sont.
///
/// La justification complete de cette forme est ecrite une seule fois, dans
/// `user-service` — `Messaging/Kafka/Outbox/OutboxUsers.cs`.
/// </summary>
public static class OutboxCommunication
{
    internal static IServiceCollection AjouterOutboxCommunication(this IServiceCollection services)
    {
        services.AddOutboxProcessor<MessagingDbContext>();
        return services;
    }
}
