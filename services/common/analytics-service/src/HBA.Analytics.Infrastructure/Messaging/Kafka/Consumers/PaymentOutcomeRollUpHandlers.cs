using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Un paiement encaissé entre dans le roll-up des paiements.</summary>
public sealed class PaymentCapturedRollUpHandler : IIntegrationEventHandler<PaymentCapturedIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public PaymentCapturedRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        PaymentCapturedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _projecteur.PaiementAsync(
            integrationEvent.OccurredOnUtc,
            IssueDePaiement.Encaisse,
            integrationEvent.Provider,
            integrationEvent.Amount,
            integrationEvent.Currency,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Un paiement échoué entre dans le roll-up des paiements.</summary>
public sealed class PaymentFailedRollUpHandler : IIntegrationEventHandler<PaymentFailedIntegrationEvent>
{
    private readonly ProjecteurDeRollUps _projecteur;
    private readonly IAnalyticsUnitOfWork _unitOfWork;

    public PaymentFailedRollUpHandler(ProjecteurDeRollUps projecteur, IAnalyticsUnitOfWork unitOfWork)
    {
        _projecteur = projecteur;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(
        PaymentFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        await _projecteur.PaiementAsync(
            integrationEvent.OccurredOnUtc,
            IssueDePaiement.Echoue,
            integrationEvent.Provider,
            integrationEvent.Amount,
            integrationEvent.Currency,
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
