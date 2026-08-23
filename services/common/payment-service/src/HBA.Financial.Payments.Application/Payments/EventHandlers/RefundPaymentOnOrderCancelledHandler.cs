using MediatR;
using Microsoft.Extensions.Logging;
using HBA.Financial.Payments.Application.Payments.Commands;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

// `IPaymentsModuleApi` EXISTE AUSSI DANS `HBA.Payments.Contracts` (partagé).
//
// C'est la même duplication que pour les événements, mais sur une INTERFACE. Le
// conteneur résout par type exact : injecter la jumelle partagée alors que
// l'installeur enregistre celle-ci ferait échouer la validation au démarrage —
// ce qui est, cette fois, une chance : sur un événement, la même erreur passe
// sans bruit.
//
// La règle reste la même : le contrat du service qui possède la donnée.
using HBA.Financial.Payments.Contracts;

namespace HBA.Financial.Payments.Application.Payments.EventHandlers;

/// <summary>
/// Commande annulée → paiement remboursé.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE MAILLON MANQUAIT, ET AUCUN AUDIT NE L'AVAIT VU.
///
/// `OrderCancelled` avait bien deux consommateurs : financial reprenait les gains
/// vendeur, communication prévenait le client. **Personne ne remboursait.**
///
/// Le monolithe le faisait ailleurs — un helper de sa composition root enchaînait
/// annulation puis `RefundPaymentCommand` dans le même geste, parce qu'il avait
/// accès aux deux modules. En microservices, order-service ne peut pas envoyer une
/// commande de financial. Le geste s'est perdu dans la découpe.
///
/// Conséquence, quelle que soit la cause de l'annulation — refus du restaurant,
/// rupture de stock, geste d'exploitation : la commande passe « annulée », le
/// client reçoit une notification d'annulation… et son argent reste encaissé.
///
/// RÉAGIR À UN FAIT, PLUTÔT QU'ÊTRE APPELÉ.
///
/// C'est financial qui possède le paiement ; c'est donc à lui de décider ce
/// qu'annuler implique. order-service annonce, il n'ordonne pas.
///
/// TROIS SITUATIONS OÙ NE RIEN FAIRE EST LA BONNE RÉPONSE.
///
/// Aucun paiement (paiement à la livraison, annulation avant encaissement), un
/// paiement non encaissé, ou un paiement déjà remboursé. Les trois sont
/// normales ; les traiter en erreur ferait rejouer le message jusqu'à la lettre
/// morte pour une commande correctement close.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class RefundPaymentOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ISender _sender;
    private readonly IPaymentsModuleApi _payments;
    private readonly ILogger<RefundPaymentOnOrderCancelledHandler> _logger;

    public RefundPaymentOnOrderCancelledHandler(
        ISender sender,
        IPaymentsModuleApi payments,
        ILogger<RefundPaymentOnOrderCancelledHandler> logger)
    {
        _sender = sender;
        _payments = payments;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var paiement = await _payments.GetPaymentByOrderAsync(e.OrderId, cancellationToken);

        if (paiement is null)
        {
            _logger.LogInformation(
                "Commande {OrderId} annulée : aucun paiement à rembourser.", e.OrderId);

            return;
        }

        // SEUL UN PAIEMENT ENCAISSÉ SE REMBOURSE.
        //
        // Redemander le remboursement d'un paiement déjà remboursé — cas du
        // rejeu — rendrait « payments.not_refundable » et ferait boucler le
        // message sur une situation parfaitement saine.
        if (!string.Equals(paiement.Status, "Captured", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Commande {OrderId} annulée : paiement {PaymentId} dans l'état {Statut}, "
                + "aucun remboursement.",
                e.OrderId, paiement.Id, paiement.Status);

            return;
        }

        var remboursement = await _sender.Send(new RefundPaymentCommand(paiement.Id), cancellationToken);

        if (remboursement.IsFailure)
        {
            var erreur = remboursement.Error;

            // ═════════════════════════════════════════════════════════════════
            // PANNE PASSAGÈRE : ON LÈVE, ET C'EST LE SEUL CAS OÙ ON LÈVE.
            //
            // `DependencyUnavailable` veut dire que le prestataire n'a pas
            // répondu — pas qu'il a refusé. La tentative suivante peut aboutir :
            // c'est exactement ce pour quoi le rejeu existe. Le consommateur
            // Kafka réessaie trois fois avec un délai croissant.
            //
            // Les exceptions de transport levées par les adaptateurs HTTP
            // (`HttpRequestException`, annulation par timeout) traversent
            // `_sender.Send` et cette méthode sans être attrapées : elles
            // aboutissent au même rejeu, par le même chemin.
            //
            // CE REJEU EST AUJOURD'HUI SANS EFFET, ET IL FAUT LE DIRE.
            //
            // `RefundPaymentCommandHandler` persiste la demande de remboursement
            // AVANT d'appeler le PSP. Ce `SaveChanges` committe du même coup la
            // trace d'inbox posée par `IntegrationEventDispatcher` : à la
            // tentative suivante, le répartiteur voit l'événement « déjà traité »
            // et saute ce gestionnaire. La levée reste le signal juste (span en
            // erreur, journal Critical du consommateur), mais tant qu'aucune
            // tâche de réconciliation ne reprend les demandes restées
            // « Processing », un remboursement interrompu ne repartira pas seul.
            // ═════════════════════════════════════════════════════════════════
            if (erreur.Type == ErrorType.DependencyUnavailable)
            {
                _logger.LogError(
                    "Remboursement INTERROMPU pour la commande annulée {OrderId} — paiement "
                    + "{PaymentId}, {Code} : {Message}. Prestataire injoignable : rejeu demandé.",
                    e.OrderId, paiement.Id, erreur.Code, erreur.Message);

                throw new InvalidOperationException(
                    $"Remboursement interrompu pour la commande {e.OrderId} : "
                    + $"{erreur.Code} — {erreur.Message}");
            }

            // ═════════════════════════════════════════════════════════════════
            // REFUS DU PRESTATAIRE : ON NE LÈVE PAS. LEVER SATURAIT LA FILE.
            //
            // Ici, le prestataire a RÉPONDU, et sa réponse est non — le cas le
            // plus fréquent étant qu'il ne sait pas rembourser du tout (FedaPay,
            // MTN, Moov, PayPal répondent « refusé » en dur). Rejouer ne change
            // rien : le refus est identique à chaque tentative. L'ancien code
            // levait, et le message repartait indéfiniment — le client n'était
            // jamais remboursé ET le consommateur se saturait d'un message qui ne
            // passerait jamais.
            //
            // L'échec est déjà ENREGISTRÉ sur le paiement par la commande
            // (`MarkRefundFailed` → `PaymentRefundFailedDomainEvent`), et ce même
            // enregistrement committe la trace d'inbox : l'offset peut avancer
            // sans que le fait ne se perde.
            //
            // Reste à le rendre VISIBLE. `Critical`, parce qu'un client attend son
            // argent et que personne ne le saura autrement : la commande est
            // close, la notification d'annulation est partie, et seul ce journal
            // dit qu'un virement manuel est dû.
            // ═════════════════════════════════════════════════════════════════
            _logger.LogCritical(
                "Paiement {PaymentId} NON REMBOURSÉ pour la commande annulée {OrderId} — "
                + "{Code} : {Message}. Le client est débité pour une commande annulée et aucun "
                + "rejeu ne corrigera cela : REMBOURSEMENT MANUEL REQUIS chez {Prestataire}.",
                paiement.Id, e.OrderId, erreur.Code, erreur.Message, paiement.Provider);

            return;
        }

        _logger.LogInformation(
            "Paiement {PaymentId} remboursé pour la commande annulée {OrderId} ({Motif}).",
            paiement.Id, e.OrderId, e.Reason);
    }
}
