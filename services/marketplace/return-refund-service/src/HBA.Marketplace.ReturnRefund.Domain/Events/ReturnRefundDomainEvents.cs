using HBA.Marketplace.ReturnRefund.Domain.Enums;
using HBA.Shared.Domain.Events;

namespace HBA.Marketplace.ReturnRefund.Domain.Events;

public sealed record ReturnRequestedDomainEvent(Guid ReturnId, Guid OrderId, Guid CustomerId, Guid SellerId) : DomainEvent;
public sealed record ReturnApprovedDomainEvent(Guid ReturnId, Guid OrderId, Guid SellerId) : DomainEvent;
public sealed record ReturnRejectedDomainEvent(Guid ReturnId, Guid OrderId, string Reason) : DomainEvent;
public sealed record ReturnShipmentRegisteredDomainEvent(Guid ReturnId, string DeliveryId) : DomainEvent;
public sealed record ReturnReceivedDomainEvent(Guid ReturnId, DateTime ReceivedAtUtc) : DomainEvent;
public sealed record ReturnInspectedDomainEvent(Guid ReturnId, InspectionCondition Condition, StockDisposition Disposition) : DomainEvent;

/// <summary>Un remboursement vient d'être DÉCIDÉ.</summary>
public sealed record RefundRequestedDomainEvent(
    Guid ReturnId,
    Guid RefundId,
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    decimal Amount,
    string Currency) : DomainEvent;

/// <summary>L'argent est PARTI, référence du prestataire à l'appui.</summary>
/// <param name="Lines">Les lignes de commande reprises par ce dossier, quantité cumulée.</param>
/// <param name="ReturnTotalRefunded">
/// Ce que ce dossier a remboursé au total, versements cumulés — pas seulement
/// celui-ci.
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

/// <summary>Une ligne reprise, telle que le dossier la connaît au moment du versement.</summary>
public sealed record RefundedLineSnapshot(Guid OrderItemId, int Quantity);

public sealed record ReturnClosedDomainEvent(Guid ReturnId, ReturnStatus FinalStatus) : DomainEvent;
