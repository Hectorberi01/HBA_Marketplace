using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// FERME LA FENETRE DE TRENTE SECONDES PENDANT LAQUELLE UN JETON REVOQUE PASSAIT.
/// </summary>
[NomDeConsommateur("HBA.Gateway.Infrastructure.Messaging.Kafka.Consumers.InvaliderLeCacheSurRevocationHandler")]
public sealed class InvaliderLeCacheSurRevocationHandler : IIntegrationEventHandler<TokenRevokedIntegrationEvent>
{
    private readonly RegistreDeRevocation _registre;
    private readonly ILogger<InvaliderLeCacheSurRevocationHandler> _logger;

    public InvaliderLeCacheSurRevocationHandler(
        RegistreDeRevocation registre,
        ILogger<InvaliderLeCacheSurRevocationHandler> logger)
    {
        _registre = registre;
        _logger = logger;
    }

    public Task HandleAsync(TokenRevokedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var evince = _registre.Revoquer(e.UserId);

        // `LogDebug` QUAND RIEN N'EST EVINCE, ET C'EST VOLONTAIRE.
        if (evince)
        {
            _logger.LogInformation(
                "Verdicts de révocation évincés pour le compte {UserId} — raison : {Reason}.",
                e.UserId, e.Reason);
        }
        else
        {
            _logger.LogDebug(
                "Révocation reçue pour le compte {UserId} : aucune entrée en cache sur cette instance.",
                e.UserId);
        }

        return Task.CompletedTask;
    }
}
