using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.
///
/// L'inbox et les abonnements ne sont pas enregistres par l'installeur — que le
/// composition root appelle toujours — mais par `AjouterMessagerieAnalytics()`,
/// qu'il peut oublier. Un oubli ne casse RIEN de visible : le service compile,
/// demarre, sert ses routes de lecture, et ne consomme plus rien. Les graphes se
/// figent sur les chiffres de la veille, et personne ne le voit avant qu'un
/// vendeur ne s'en etonne.
///
/// CE SERVICE N'A PAS D'OUTBOX, ce qui rend cette garde PLUS importante ici
/// qu'ailleurs : dans un service qui publie, l'oubli finit par se voir parce que
/// quelqu'un attend un message. Ici, personne n'attend rien.
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
internal sealed class GardeDeCablage(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AbonnementsKafka>() is null)
        {
            throw new InvalidOperationException(
                "Le module de messagerie de ce service n'est pas enregistre : aucun "
                + "AbonnementsKafka dans le conteneur. Sans lui, ce service ne consomme "
                + "aucun evenement et ses roll-ups restent figes. Ajouter "
                + "« builder.Services.AjouterMessagerieAnalytics(); » dans Program.cs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
