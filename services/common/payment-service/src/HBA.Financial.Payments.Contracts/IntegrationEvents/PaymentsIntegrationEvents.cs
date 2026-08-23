using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Payments.Contracts.IntegrationEvents;

/// <summary>
/// Paiement encaissé. Consommé par Ordering (confirmation de la commande).
///
/// LA CLASSE DIT « Captured », LE CONTRAT DIT « succeeded ».
///
/// Le §10.12 nomme cet événement `payment.succeeded`. Le domaine, lui, distingue
/// l'autorisation de la capture — une nuance réelle chez les prestataires et que
/// la spec ne connaît pas. Renommer la classe effacerait cette distinction dans le
/// code ; renommer l'événement casserait le contrat. `[HbaEvent]` permet de garder
/// les deux : le nom métier est celui du cahier des charges, le nom de classe reste
/// celui du domaine.
/// </summary>
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
}

/// <summary>Paiement échoué. Consommé par Ordering (annulation + libération du stock).</summary>
[HbaEvent("payment.failed", Version = 1, AggregateType = "Payment")]
public sealed record PaymentFailedIntegrationEvent : IntegrationEvent
{
    public required Guid PaymentId { get; init; }
    public required Guid OrderId { get; init; }
    public required string OrderType { get; init; }
    public required string Reason { get; init; }
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
    /// chaque événement se déclare indépendamment. C'est le test de contrat qui l'a
    /// trouvé, en exigeant la propriété sur les QUATRE.
    /// </summary>
    public required string OrderType { get; init; }

    // AJOUTÉS PARCE QUE CET ÉVÉNEMENT N'AVAIT AUCUN CONSOMMATEUR.
    //
    // Il était publié dans le vide : l'acheteur n'apprenait jamais que son
    // argent lui était rendu. Le prévenir demande de savoir qui il est et
    // combien lui revient — sans quoi communication-service devrait interroger
    // deux autres services pour chaque remboursement.
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


/// <summary>
/// Intention de paiement créée (§10.12, `payment.created`). Il manquait : rien ne
/// signalait qu'un paiement avait été ouvert, seulement son issue.
///
/// AUCUNE DONNÉE DE CARTE, AUCUN JETON DE PRESTATAIRE.
///
/// Le §8 est explicite : aucune donnée de carte brute ne circule, et le §19.7
/// interdit les secrets dans les événements. La référence prestataire n'y est pas
/// non plus — elle sert à rejouer un appel chez le fournisseur, ce qui n'est
/// l'affaire de personne d'autre que payment-service.
/// </summary>
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
