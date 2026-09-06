using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// REFUSE LE DÉMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ÉTÉ BRANCHÉ.
///
/// LE TROU QUE CETTE CLASSE FERME. Descendre l'outbox, l'inbox et les
/// abonnements dans `Messaging/Kafka/` a un coût : ils ne sont plus enregistrés
/// par `UsersModuleInstaller`, que le composition root appelle toujours, mais par
/// `AjouterMessagerieUsers()`, qu'il peut oublier. Un service qui l'oublie
/// COMPILE, DÉMARRE, sert ses routes HTTP, écrit dans l'outbox — et n'émet ni ne
/// consomme plus rien. Aucune exception, aucun journal : exactement la panne qui
/// a laissé `users.user_profiles` vide pendant des semaines pendant que
/// `identity.users` se remplissait.
///
/// La garde est enregistrée par l'INSTALLEUR, pas par le module : c'est tout
/// l'intérêt. Elle est présente même quand ce qu'elle vérifie est absent.
///
/// `IHostedService` ET NON `BackgroundService`. Une exception levée dans
/// `StartAsync` arrête l'hôte ; la même dans `ExecuteAsync` d'un
/// `BackgroundService` est avalée et laisse le service debout, en panne muette.
///
/// CE QU'ELLE NE COUVRE PAS. Elle vérifie que le module a été appelé, pas qu'il
/// est COMPLET : un `AjouterMessagerieUsers` qui oublierait un gestionnaire, ou
/// un sujet, passe cette garde sans rien dire. Elle ne remplace pas la lecture
/// côte à côte de `SujetsUsers` et de `Consumers/`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class GardeDeCablage(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AbonnementsKafka>() is null)
        {
            throw new InvalidOperationException(
                "Le module de messagerie de user-service n'est pas enregistré : "
                + "aucun AbonnementsKafka dans le conteneur. Sans lui, ce service "
                + "ne consomme aucun événement et son outbox n'est jamais vidée. "
                + "Ajouter « builder.Services.AjouterMessagerieUsers(); » dans "
                + "Program.cs, après AddHbaService.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
