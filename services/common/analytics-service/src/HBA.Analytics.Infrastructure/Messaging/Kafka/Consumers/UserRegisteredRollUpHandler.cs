using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Un compte créé compte dans les inscriptions acheteur du jour.</summary>
public sealed class UserRegisteredRollUpHandler : IIntegrationEventHandler<UserRegisteredIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public UserRegisteredRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        UserRegisteredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _projecteur.InscriptionAsync(
            integrationEvent.OccurredOnUtc, NatureDInscription.Acheteur, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
