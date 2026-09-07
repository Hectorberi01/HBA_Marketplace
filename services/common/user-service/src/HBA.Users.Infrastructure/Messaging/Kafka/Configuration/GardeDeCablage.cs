using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Users.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>REFUSE LE DÉMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ÉTÉ BRANCHÉ.</summary>
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
