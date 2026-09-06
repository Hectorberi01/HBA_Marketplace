using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.
///
/// L'outbox n'est plus enregistree par l'installeur — que le composition root
/// appelle toujours — mais par `AjouterMessagerieCommunication()`, qu'il peut
/// oublier. Un oubli ne casserait rien de visible : les messages internes
/// s'ecriraient dans l'outbox et n'en sortiraient jamais.
///
/// `IHostedService` et non `BackgroundService` : une exception levee dans
/// `StartAsync` arrete l'hote, la meme dans `ExecuteAsync` est avalee.
///
/// CE QU'ELLE NE COUVRE PAS. Elle verifie que le module a ete appele, pas qu'il
/// est complet.
/// </summary>
internal sealed class GardeDeCablage(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AbonnementsKafka>() is null)
        {
            throw new InvalidOperationException(
                "Le module de messagerie de la messagerie interne n'est pas enregistre : aucun "
                + "AbonnementsKafka dans le conteneur. Sans lui, son outbox n'est jamais videe. "
                + "Ajouter « builder.Services.AjouterMessagerieCommunication(); » dans Program.cs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
