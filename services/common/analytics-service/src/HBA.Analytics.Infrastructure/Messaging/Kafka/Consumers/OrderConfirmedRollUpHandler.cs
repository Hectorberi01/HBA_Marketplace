using HBA.Analytics.Application.Abstractions;
using HBA.Analytics.Application.RollUps.Projections;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;

namespace HBA.Analytics.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Une commande confirmée entre dans les roll-ups vendeur et plateforme.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// « CONFIRMÉE » ET NON « PLACÉE » — LA DIFFÉRENCE EST TOUT LE CHIFFRE.
///
/// `OrderPlaced` part au checkout, avant le paiement. Une commande placée puis
/// abandonnée à l'écran de paiement compterait alors comme une vente. Le fil est
/// explicite : `PaymentCaptured` → `ConfirmOrderPaymentCommand` →
/// `OrderConfirmed`. Seule une vente réellement encaissée arrive ici — c'est le
/// même raisonnement qui fait décompter les coupons à la confirmation et pas au
/// checkout.
///
/// LA JOURNÉE EST CELLE DE L'ÉVÉNEMENT, PAS CELLE DE LA CONSOMMATION.
///
/// `OccurredOnUtc` vient du producteur. Prendre `DateTime.UtcNow` ici ferait
/// basculer de journée tout ce qui a attendu dans l'outbox ou dans le courtier —
/// et, lors d'un rattrapage après panne, écraserait une semaine de ventes sur
/// le jour du redémarrage.
///
/// CE GESTIONNAIRE N'EST PAS IDEMPOTENT PAR LUI-MÊME, ET NE PEUT PAS L'ÊTRE.
///
/// Il incrémente. Sa protection est la table `consumer_inbox`, posée par le
/// dispatcher AVANT l'appel et committée par le `SaveChangesAsync` ci-dessous,
/// dans la même transaction. C'est aussi pourquoi il DOIT écrire en base : un
/// gestionnaire qui n'écrit rien laisse sa trace en attente, jamais committée,
/// donc rejouable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
        //
        // `OrderSellerShare` appartient a order-service. Le laisser descendre
        // jusqu'au projeteur ferait entrer `HBA.Order.Contracts` dans la couche
        // qui porte les requetes de lecture, et chaque nouveau graphe y
        // ajouterait un contrat de plus.
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
