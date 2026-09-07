using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Drivers.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Un compte livreur ouvert compte dans les inscriptions livreur du jour.</summary>
public sealed class DriverCreatedRollUpHandler : IIntegrationEventHandler<DriverCreatedIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public DriverCreatedRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    // `DriverCreated` et non `DriverVerified` : c'est l'ouverture du dossier qui
    // est une inscription, comme `SellerRegistered` cote vendeur. La verification
    // est une etape d'apres, et en faire le signal donnerait une courbe qui
    // mesure le delai de traitement du back-office, pas les arrivees.
    public async Task HandleAsync(
        DriverCreatedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _projecteur.InscriptionAsync(
            integrationEvent.OccurredOnUtc, NatureDInscription.Livreur, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
