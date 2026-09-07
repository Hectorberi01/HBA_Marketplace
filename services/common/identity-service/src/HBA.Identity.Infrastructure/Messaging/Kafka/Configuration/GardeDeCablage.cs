using HBA.Shared.Infrastructure.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HBA.Identity.Infrastructure.Messaging.Kafka.Configuration;

/// <summary>REFUSE LE DEMARRAGE SI LE MODULE DE MESSAGERIE N'A PAS ETE BRANCHE.</summary>
internal sealed class GardeDeCablage(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<AbonnementsKafka>() is null)
        {
            throw new InvalidOperationException(
                "Le module de messagerie de ce service n'est pas enregistre : aucun "
                + "AbonnementsKafka dans le conteneur. Sans lui, ce service ne consomme "
                + "aucun evenement et son outbox n'est jamais videe. Ajouter "
                + "« builder.Services.AjouterMessagerieIdentity(); » dans Program.cs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
