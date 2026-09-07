using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Un dossier vendeur ouvert compte dans les inscriptions vendeur du jour.</summary>
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
