using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HBA.Routes.Infrastructure.Messaging.Kafka.Producers;

using HBA.Routes.Infrastructure.Persistence.Outbox;
using HBA.Routes.Infrastructure.Persistence.Inbox;
namespace HBA.Routes.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.
///
/// L'outbox, l'inbox et les abonnements ne sont plus enregistres par
/// l'installeur — que le composition root appelle toujours — mais par
/// `AjouterMessagerieDeliveryRoute()`, qu'il peut oublier. Un oubli ne casse RIEN de
/// visible : le service compile, demarre, sert ses routes, et n'emet ni ne
/// consomme plus rien.
///
/// La garde est enregistree par l'INSTALLEUR : elle doit exister quand ce
/// qu'elle verifie est absent.
///
/// `IHostedService` et non `BackgroundService` : une exception levee dans
/// `StartAsync` arrete l'hote, la meme dans `ExecuteAsync` est avalee.
///
/// CE QU'ELLE NE COUVRE PAS. Elle verifie que le module a ete appele, pas qu'il
/// est complet : un sujet ou un gestionnaire oublie passe sans rien dire.
/// </summary>
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

        // ═════════════════════════════════════════════════════════════════════
        // CE SERVICE PUBLIE TROIS EVENEMENTS QUI NE PARTENT NULLE PART.
        //
        // route-service garde ses routes EN MEMOIRE : pas de `ModuleDbContext`,
        // donc pas de table d'outbox, donc aucun processeur pour la vider.
        // `IIntegrationEventPublisher` est resolu sur `IntegrationEventQueue`,
        // une file scopee que personne ne draine : `PublishAsync` rend
        // `Task.CompletedTask` et le message part avec la portee.
        //
        // POURQUOI UN JOURNAL ET PAS UNE EXCEPTION. Ce service tourne aujourd'hui
        // et rend son service — le calcul d'itineraire est synchrone, par gRPC.
        // Le faire echouer au demarrage transformerait un defaut connu et sans
        // consequence actuelle en panne. Les trois evenements n'ont d'ailleurs
        // aucun consommateur.
        //
        // CE QUE CE MESSAGE DOIT PROVOQUER. Soit donner une base a ce service et
        // cabler son outbox, soit retirer les trois publications. Les laisser
        // sans le dire etait le pire des trois : le code affirme publier, et rien
        // ne sort.
        // ═════════════════════════════════════════════════════════════════════
        if (EvenementsPublies.Types.Count > 0 && services.GetService<IOutboxDbContext>() is null)
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
