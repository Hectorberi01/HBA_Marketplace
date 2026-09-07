using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Un compte créé compte dans les inscriptions acheteur du jour.
/// </summary>
/// <remarks>
/// « ACHETEUR » EST UN RACCOURCI, ET IL FAUT LE SAVOIR.
///
/// `UserRegistered` dit qu'un COMPTE a été créé — pas que son titulaire achètera.
/// Identity ne sait pas, au moment de l'inscription, ce que ce compte deviendra :
/// acheteur, vendeur, livreur. Ranger la ligne sous « Buyer » est donc une
/// convention de lecture, pas un fait, et c'est pourquoi le graphe l'appelle
/// « inscriptions » et non « nouveaux acheteurs ».
///
/// NI L'E-MAIL NI LE NOM NE SONT LUS. Ce service compte ; il ne constitue pas un
/// second annuaire des comptes. L'événement les porte parce que
/// notification-service en a besoin — pas parce que tout consommateur doit les
/// garder.
/// </remarks>
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
