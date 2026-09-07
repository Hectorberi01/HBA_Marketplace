using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Communication.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.</summary>
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
