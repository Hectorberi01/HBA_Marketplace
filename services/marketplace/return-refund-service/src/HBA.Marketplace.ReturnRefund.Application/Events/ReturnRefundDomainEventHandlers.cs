using HBA.Marketplace.ReturnRefund.Domain.Events;
using HBA.Returns.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Marketplace.ReturnRefund.Application.Events;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX ÉVÉNEMENTS DU MODULE RETOURS N'ÉTAIENT PUBLIÉS PAR PERSONNE.
///
/// `ReturnRefundApprovedIntegrationEvent` ET `ReturnRefundedIntegrationEvent`
/// EXISTAIENT, AVEC LEURS CONSOMMATEURS, ET AUCUN ÉMETTEUR.
///
/// Trois gestionnaires les attendaient, enregistrés et prêts :
///
///   • `ReverseEarningsOnReturnRefundedHandler` (wallet-service) — contre-passe le
///     gain du vendeur et restitue la commission. Sans lui, la marchandise revient,
///     le client est remboursé, et le vendeur GARDE sa vente : la plateforme paie
///     deux fois, à chaque retour, sans que rien ne l'indique aux comptes.
///   • `ReturnRefundApprovedNotificationHandler` (notification-service) — dit au
///     client que sa demande est acceptée et que le versement suit. C'est le
///     message qui évite qu'il ouvre un litige pendant l'attente.
///   • `ReturnRefundedNotificationHandler` (notification-service) — prévient
///     l'acheteur que l'argent est parti, et le vendeur qu'il vient d'être débité.
///
/// Les trois étaient branchés sur un fil qui ne portait aucun courant.
///
/// POURQUOI DANS UN GESTIONNAIRE DE DOMAINE, ET NON DANS LE HANDLER DE COMMANDE.
///
/// `ModuleDbContext.SaveChangesAsync` dispatche les événements de domaine PUIS
/// draine la file d'intégration vers l'outbox, le tout avant le
/// `base.SaveChangesAsync`. L'événement publié ici part donc en base dans LA MÊME
/// TRANSACTION que le changement d'état de l'agrégat. Publier depuis le handler de
/// commande, après le `SaveChanges`, ouvrirait la fenêtre inverse : un
/// remboursement écrit et un message jamais parti — c'est-à-dire un vendeur jamais
/// contre-passé, découvert au rapprochement.
///
/// GARDE D'IDEMPOTENCE (lot 2.1) : CE QUI EST COUVERT ET CE QUI NE L'EST PAS.
///
/// Ces deux classes sont des gestionnaires de DOMAINE, in-process, à l'intérieur
/// d'un `SaveChanges` : ni inbox, ni rejeu, la question ne se pose pas de ce
/// côté-ci. Elle se pose côté CONSOMMATEURS, et elle est réglée : le gestionnaire
/// de wallet écrit son grand livre puis appelle `SaveChangesAsync` — sa trace
/// d'inbox part avec — et porte en plus son propre verrou
/// (`RefundAlreadyReversedAsync`). Les gestionnaires de notification écrivent
/// leurs notifications par `NotificationDispatcher`, donc committent aussi leur
/// trace.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RefundRequestedDomainEventHandler : IDomainEventHandler<RefundRequestedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RefundRequestedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(RefundRequestedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundApprovedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.CustomerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.Amount,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>
/// L'argent est parti : wallet-service contre-passe, notification-service prévient.
///
/// <para>
/// `RefundReference` N'EST PAS DÉCORATIVE. C'est la référence rendue par le
/// prestataire — la seule preuve que le versement a eu lieu chez lui. Elle est
/// exigée par le contrat (`required`) précisément pour qu'un remboursement ne
/// puisse pas se déclarer effectué sans elle.
/// </para>
/// </summary>
public sealed class RefundSucceededDomainEventHandler : IDomainEventHandler<RefundSucceededDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public RefundSucceededDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(RefundSucceededDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new ReturnRefundedIntegrationEvent
            {
                ReturnRequestId = domainEvent.ReturnId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.CustomerId,
                SellerId = domainEvent.SellerId,
                RefundAmount = domainEvent.Amount,
                Currency = domainEvent.Currency,
                RefundReference = domainEvent.ProviderRefundId,

                // CES DEUX CHAMPS SONT LE VOLET RETURN-REFUND D'ISSUE-014.
                //
                // Order-service ne possède pas les retours : sans eux, il répond
                // `AlreadyReturnedQuantity: 0` et `AlreadyRefundedAmount: 0` — et
                // le même article se rembourse indéfiniment. Ils sont CUMULÉS pour
                // le dossier ; le consommateur pose la valeur au lieu de
                // l'additionner, de sorte qu'un rejeu n'impute rien de plus.
                Lines = domainEvent.Lines
                    .Select(l => new ReturnedOrderLine { OrderItemId = l.OrderItemId, Quantity = l.Quantity })
                    .ToList(),
                ReturnTotalRefundedAmount = domainEvent.ReturnTotalRefunded
            },
            cancellationToken);
}
