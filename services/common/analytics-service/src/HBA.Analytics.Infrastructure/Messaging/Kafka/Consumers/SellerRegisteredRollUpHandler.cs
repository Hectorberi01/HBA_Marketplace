using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Un dossier vendeur ouvert compte dans les inscriptions vendeur du jour.
/// </summary>
/// <remarks>
/// `SellerRegistered` ET NON `SellerActivated` : le graphe demandé est celui des
/// INSCRIPTIONS, pas des activations. Les deux existent et ne mesurent pas la
/// même chose — entre les deux il y a le KYB, dont le délai est précisément ce
/// qu'on voudra regarder un jour. Compter l'activation ferait disparaître des
/// courbes tous les dossiers en attente.
///
/// CE VENDEUR EST DÉJÀ COMPTÉ COMME ACHETEUR, un jour antérieur ou le même :
/// il s'est inscrit d'abord comme utilisateur. Voir `NatureDInscription` — les
/// deux séries ne s'additionnent pas.
/// </remarks>
public sealed class SellerRegisteredRollUpHandler : IIntegrationEventHandler<SellerRegisteredIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public SellerRegisteredRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        SellerRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _projecteur.InscriptionAsync(
            integrationEvent.OccurredOnUtc, NatureDInscription.Vendeur, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
