using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Payments.Contracts.IntegrationEvents;

/// <summary>Paiement encaissé. Consommé par Ordering (confirmation de la commande).</summary>
[HbaEvent("payment.succeeded", Version = 1, AggregateType = "Payment")]
public sealed record PaymentCapturedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }

    /// <summary>
    /// `MARKETPLACE` ou `FOOD`. Sans lui, les deux services de commande doivent
    /// chercher `OrderId` chacun chez soi, et celui qui ne le trouve pas ne peut
    /// pas distinguer « pas pour moi » de « ma commande a disparu ».
    /// </summary>
    public required string OrderType { get; init; }

    /// <summary>
    /// Le prestataire qui a traité l'opération — « fedapay », « kkiapay »… <c>
    /// null</c> pour un message émis avant l'ajout du champ.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Le montant de l'opération.</summary>
    public decimal? Amount { get; init; }

    /// <summary>La devise du montant ci-dessus.</summary>
    public string? Currency { get; init; }
}

/// <summary>Paiement échoué. Consommé par Ordering (annulation + libération du stock).</summary>
[HbaEvent("payment.failed", Version = 1, AggregateType = "Payment")]
public sealed record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string OrderType { get; init; }
    public required string Reason { get; init; }

    /// <summary>
    /// Le prestataire qui a traité l'opération — « fedapay », « kkiapay »… <c>
    /// null</c> pour un message émis avant l'ajout du champ.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Le montant de l'opération.</summary>
    public decimal? Amount { get; init; }

    /// <summary>La devise du montant ci-dessus.</summary>
    public string? Currency { get; init; }
}

/// <summary>Paiement remboursé. Consommé par Notifications / comptabilité.</summary>
[HbaEvent("payment.refunded", Version = 1, AggregateType = "Payment")]
public sealed record PaymentRefundedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RefundId { get; init; }
    public Guid? ReturnId { get; init; }
    public Guid? ExternalRefundId { get; init; }

    /// <summary>
    /// `MARKETPLACE` ou `FOOD`. Il manquait ici alors qu'il était sur les trois
    /// autres événements — un oubli qu'aucun compilateur ne pouvait voir, puisque
    /// chaque événement se déclare indépendamment.
    /// </summary>
    public required string OrderType { get; init; }

    // AJOUTÉS PARCE QUE CET ÉVÉNEMENT N'AVAIT AUCUN CONSOMMATEUR.
    public required Guid BuyerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string ProviderRefundId { get; init; }
}

/// <summary>Echec de remboursement publie pour reconciliation ReturnRefund / support.</summary>
[HbaEvent("payment.refund.failed", Version = 1, AggregateType = "Payment")]
public sealed record PaymentRefundFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string OrderType { get; init; }
    public required Guid RefundId { get; init; }
    public Guid? ReturnId { get; init; }
    public Guid? ExternalRefundId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Reason { get; init; }
}


/// <summary>Intention de paiement créée (§10.12, `payment.created`).</summary>
[HbaEvent("payment.created", Version = 1, AggregateType = "Payment")]
public sealed record PaymentCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string OrderType { get; init; }
    public required Guid BuyerId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }

    /// <summary>`MTN_MOMO`, `MOOV_MONEY`, `STRIPE`… le prestataire retenu.</summary>
    public required string Provider { get; init; }
}
