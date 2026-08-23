using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Events;

namespace HBA.Marketplace.ReturnRefund.Domain.Events;

public sealed record ReturnRequestedDomainEvent(Guid ReturnId, Guid OrderId, Guid CustomerId, Guid SellerId) : DomainEvent;
public sealed record ReturnApprovedDomainEvent(Guid ReturnId, Guid OrderId, Guid SellerId) : DomainEvent;
public sealed record ReturnRejectedDomainEvent(Guid ReturnId, Guid OrderId, string Reason) : DomainEvent;
public sealed record ReturnShipmentRegisteredDomainEvent(Guid ReturnId, string DeliveryId) : DomainEvent;
public sealed record ReturnReceivedDomainEvent(Guid ReturnId, DateTime ReceivedAtUtc) : DomainEvent;
public sealed record ReturnInspectedDomainEvent(Guid ReturnId, InspectionCondition Condition, StockDisposition Disposition) : DomainEvent;

/// <summary>
/// Un remboursement vient d'être DÉCIDÉ. L'argent n'est pas parti.
///
/// <para>
/// `OrderId`, `CustomerId` et `SellerId` ont été AJOUTÉS parce que sans eux
/// l'événement ne pouvait rien déclencher. `ReturnRefundApprovedIntegrationEvent`
/// — le message qui prévient l'acheteur que sa demande est acceptée — exige les
/// trois. Le gestionnaire aurait dû recharger l'agrégat pour les retrouver, en
/// pleine séquence de `SaveChanges`, alors que l'agrégat qui lève l'événement les
/// a sous la main.
/// </para>
///
/// <para>
/// C'est un événement de DOMAINE : il ne quitte jamais le module, aucune règle
/// additive ne s'y applique (décision D32 vise les `*IntegrationEvent`).
/// </para>
/// </summary>
public sealed record RefundRequestedDomainEvent(
    Guid ReturnId,
    Guid RefundId,
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    decimal Amount,
    string Currency) : DomainEvent;

/// <summary>
/// L'argent est PARTI, référence du prestataire à l'appui.
///
/// <para>
/// Mêmes ajouts, même raison : c'est lui qui devient
/// `ReturnRefundedIntegrationEvent`, l'événement sur lequel wallet-service
/// contre-passe le gain vendeur et la commission. Un montant absent, et la
/// contre-passation ne peut pas être calculée au prorata.
/// </para>
/// </summary>
/// <param name="Lines">
/// Les lignes de commande reprises par ce dossier, quantité cumulée. Elles
/// existent pour qu'order-service cesse de répondre `AlreadyReturnedQuantity: 0`
/// (ISSUE-014) : sans elles, rien ne lui apprend jamais qu'un article est déjà
/// revenu, et le même exemplaire se rembourse autant de fois qu'on ouvre de
/// dossiers.
/// </param>
/// <param name="ReturnTotalRefunded">
/// Ce que ce dossier a remboursé au total, versements cumulés — pas seulement
/// celui-ci. Le consommateur POSE cette valeur par dossier et somme les dossiers,
/// au lieu d'additionner les messages : un rejeu ne double alors rien.
/// </param>
public sealed record RefundSucceededDomainEvent(
    Guid ReturnId,
    Guid RefundId,
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    decimal Amount,
    string Currency,
    string ProviderRefundId,
    IReadOnlyCollection<RefundedLineSnapshot> Lines,
    decimal ReturnTotalRefunded) : DomainEvent;

/// <summary>
/// Une ligne reprise, telle que le dossier la connaît au moment du versement.
///
/// <para>
/// `OrderItemId`, PAS `ProductId`. C'est l'identifiant de la ligne chez
/// order-service ; deux lignes d'une même commande peuvent porter le même
/// produit, et le rapprochement se ferait alors sur la mauvaise.
/// </para>
/// </summary>
public sealed record RefundedLineSnapshot(Guid OrderItemId, int Quantity);

public sealed record ReturnClosedDomainEvent(Guid ReturnId, ReturnStatus FinalStatus) : DomainEvent;
