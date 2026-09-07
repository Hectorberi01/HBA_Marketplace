using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Une commande confirmée entre dans les roll-ups vendeur et plateforme.</summary>
public sealed class OrderConfirmedRollUpHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public OrderConfirmedRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LA TRADUCTION EST ICI, ET C'EST LA FRONTIERE DE LA COUCHE APPLICATION.
        var parts = integrationEvent.SellerShares
            .Select(part => new PartDeVendeur(part.SellerId, part.ItemCount, part.Amount))
            .ToArray();

        await _projecteur.CommandeConfirmeeAsync(
            integrationEvent.OccurredOnUtc,
            integrationEvent.Currency,
            integrationEvent.Kind,
            parts,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
