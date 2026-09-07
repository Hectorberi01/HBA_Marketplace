using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Deliveries.Contracts.IntegrationEvents;
using HBA.Shared.Application.Messaging;
using HBA.Shared.IntegrationEvents;
using HBA.Financial.Wallet.Application.Wallets;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>La course est remise : le livreur est payé.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Wallet.Application.Earnings.CreditDriverOnDeliveryCompletedHandler")]
public sealed class CreditDriverOnDeliveryCompletedHandler
    : IIntegrationEventHandler<DeliveryCompletedIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly ILogger<CreditDriverOnDeliveryCompletedHandler> _logger;

    public CreditDriverOnDeliveryCompletedHandler(
        ISender sender, ILogger<CreditDriverOnDeliveryCompletedHandler> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task HandleAsync(
        DeliveryCompletedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent.DriverId == Guid.Empty)
        {
            // `MarkDelivered` déréférence `AssignedDriverId!` : une course sans
            // livreur ne peut pas atteindre cet état.
            _logger.LogError(
                "Course {DeliveryId} remise SANS livreur affecté : aucun gain ne peut être versé.",
                integrationEvent.DeliveryId);

            return;
        }

        if (integrationEvent.DriverEarning is not { } gain)
        {
            // NUL N'EST PAS ZÉRO, ET C'EST TOUT L'INTÉRÊT DE LA DISTINCTION.
            _logger.LogError(
                "Course {DeliveryId} remise par le livreur {DriverId} SANS gain calculé — "
                + "la course n'avait aucun prix. Le livreur a roulé et n'est pas payé.",
                integrationEvent.DeliveryId, integrationEvent.DriverId);

            return;
        }

        if (gain <= 0m)
        {
            // Une part à zéro ou négative vient d'un taux ou d'un prix aberrant.
            _logger.LogError(
                "Course {DeliveryId} : gain de {Gain} pour le livreur {DriverId} — "
                + "montant non versable. Vérifiez le prix de la course et la part livreur.",
                integrationEvent.DeliveryId, gain, integrationEvent.DriverId);

            return;
        }

        var result = await _sender.Send(
            new CreditDriverEarningCommand(
                integrationEvent.DriverId,
                integrationEvent.DeliveryId,
                gain,
                integrationEvent.Currency),
            cancellationToken);

        // NE JAMAIS ÉCRIRE `=> _sender.Send(...)` ICI.
        SagaOutcome.Exiger(
            result, _logger,
            "créditer le gain du livreur — SANS ELLE, LE COURSIER A ROULÉ POUR RIEN",
            integrationEvent.DriverId, integrationEvent.DeliveryId, gain);
    }
}
