using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Une commande annulée débite les vendeurs qui y avaient une part.</summary>
public sealed class OrderCancelledRollUpHandler : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public OrderCancelledRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // LA TRADUCTION EST ICI, comme pour la confirmation : `OrderSellerShare`
        // appartient a order-service, et n'a pas a descendre dans la couche qui
        // porte les requetes de lecture.
        var parts = integrationEvent.SellerShares
            ?.Select(part => new PartDeVendeur(part.SellerId, part.ItemCount, part.Amount))
            .ToArray();

        await _projecteur.CommandeAnnuleeAsync(
            integrationEvent.OccurredOnUtc,
            integrationEvent.Currency,
            parts,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
