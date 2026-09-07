using HBA.Shared.Domain.Events;

namespace HBA.Financial.Payments.Domain.Payments.Events;

// `OrderType` TRAVERSE TOUTE LA CHAÎNE, DU DOMAINE À KAFKA.
/// <summary>Un paiement a été initié pour une commande.</summary>
public sealed record PaymentInitiatedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, Guid BuyerId,
    decimal Amount, string Currency, string Provider) : DomainEvent;

/// <summary>Le paiement a été encaissé — déclenche la confirmation de la commande.</summary>
public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Provider, string Method,
    decimal Amount, string Currency) : DomainEvent;

/// <summary>Le paiement a échoué — déclenche l'annulation de la commande.</summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Reason, string Provider,
    string Method, string Currency, decimal Amount) : DomainEvent;

/// <summary>Le paiement a été remboursé.</summary>
public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, Guid BuyerId, string Provider,
    decimal Amount, string Currency, Guid RefundId, Guid? ReturnId, Guid? ExternalRefundId,
    string IdempotencyKey, string ProviderRefundId) : DomainEvent;

public sealed record PaymentRefundFailedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Provider,
    decimal Amount, string Currency, Guid RefundId, Guid? ReturnId, Guid? ExternalRefundId,
    string IdempotencyKey, string Reason) : DomainEvent;
