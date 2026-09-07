using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Une commande annulée débite les vendeurs qui y avaient une part.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// IL PEUT NE RIEN ÉCRIRE, ET C'EST LE SEUL GESTIONNAIRE DE CE SERVICE DANS CE
///    CAS. IL FAUT SAVOIR CE QUE ÇA COÛTE.
///
/// `SellerShares` est nulle sur un message d'avant le lot 2, et vide pour une
/// commande de repas. Le projecteur n'écrit alors aucune ligne — donc
/// `SaveChangesAsync` ne persiste rien — donc LA TRACE D'INBOX N'EST PAS
/// COMMITTÉE. Le dispatcher l'avait pourtant ajoutée avant l'appel.
///
/// La conséquence est exactement celle que `IntegrationEventDispatcher` décrit
/// dans son encadré « ce qui reste découvert » : au rejeu, ce gestionnaire
/// refera son effet. Ici cet effet est RIEN, donc le rejeu est sans conséquence
/// — c'est la seule raison pour laquelle on peut s'en contenter, et elle cesse
/// d'être vraie le jour où ce gestionnaire écrira quelque chose dans le cas
/// vide.
///
/// `SaveChangesAsync` EST APPELÉ QUAND MÊME. Sur un cas vide il ne coûte qu'un
/// aller-retour à vide ; le retirer ferait dépendre la persistance de la trace
/// d'une condition écrite ailleurs, dans le projecteur.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
