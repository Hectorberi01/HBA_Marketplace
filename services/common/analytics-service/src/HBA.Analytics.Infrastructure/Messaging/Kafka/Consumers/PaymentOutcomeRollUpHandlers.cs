using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Analytics.Domain.RollUps;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Un paiement encaissé entre dans le roll-up des paiements.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX CLASSES POUR DEUX ÉVÉNEMENTS, ET UNE SEULE POUR LES DEUX AURAIT ÉTÉ UNE
///    ERREUR.
///
/// Une classe implémentant les deux `IIntegrationEventHandler<>` porterait UN
/// SEUL nom de consommateur dans `consumer_inbox` — c'est le nom complet du TYPE
/// que le dispatcher y écrit. Une capture et un échec partageraient alors la même
/// clé d'idempotence pour deux événements distincts, ce qui est correct par
/// accident aujourd'hui (les `eventId` diffèrent) et faux dès qu'on voudra
/// rejouer une famille sans l'autre.
///
/// LE COMPTE EST EXACT MÊME QUAND LE MONTANT MANQUE, et c'est ce qui compte pour
/// un taux : le dénominateur reste complet. Voir `FournisseurDePaiement`.
///
/// CE GESTIONNAIRE NE CONFIRME AUCUNE COMMANDE. C'est le travail de
/// `ConfirmOrderOnPaymentCapturedHandler`, chez order-service. Ici on compte, et
/// rien d'autre — un service d'analytique qui agirait sur le métier serait un
/// second chemin de décision que personne ne relirait.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// Un paiement échoué entre dans le roll-up des paiements.
/// </summary>
/// <remarks>
/// `Reason` N'EST PAS RANGÉ, ET CE N'EST PAS UN OUBLI. C'est du texte libre venu
/// du prestataire : le mettre en clé produirait autant de séries que de
/// formulations, et une colonne le rendrait non agrégeable. Le jour où l'on
/// voudra « pourquoi ça échoue », il faudra un vocabulaire fermé côté
/// payment-service — pas un `GROUP BY` sur une phrase.
/// </remarks>
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
