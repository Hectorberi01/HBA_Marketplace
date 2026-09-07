using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HBA.Routes.Infrastructure.Messaging.Kafka.Producers;

namespace HBA.Routes.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.</summary>
internal sealed class GardeDeCablage(
    IServiceProvider services,
    ILogger<GardeDeCablage> journal) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AbonnementsKafka>() is null)
        {
            throw new InvalidOperationException(
                "Le module de messagerie de ce service n'est pas enregistre : aucun "
                + "AbonnementsKafka dans le conteneur. Sans lui, ce service ne consomme "
                + "aucun evenement et son outbox n'est jamais videe. Ajouter "
                + "« builder.Services.AjouterMessagerieDeliveryRoute(); » dans Program.cs.");
        }

        // CE SERVICE PUBLIE TROIS EVENEMENTS QUI NE PARTENT NULLE PART.
        if (EvenementsPublies.Types.Count > 0)
        {
            journal.LogCritical(
                "PUBLICATIONS SANS OUTBOX : ce service déclare publier {Nombre} événement(s) "
                + "et n'a pas de base, donc pas de table d'outbox. `PublishAsync` n'écrit nulle "
                + "part et le message est perdu à la fermeture de la portée. Voir "
                + "`Messaging/Kafka/Producers/EvenementsPublies`.",
                EvenementsPublies.Types.Count);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
