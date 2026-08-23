using HBA.Shared.Domain.Events;

namespace HBA.Financial.Payments.Domain.Payments.Events;

/// <summary>Un paiement a été initié pour une commande.</summary>
// `OrderType` TRAVERSE TOUTE LA CHAÎNE, DU DOMAINE À KAFKA.
//
// L'ajouter seulement sur l'événement d'intégration aurait obligé le handler à le
// relire en base pour le remplir — une requête de plus, et surtout une occasion de
// lire un état déjà modifié depuis. L'événement de domaine porte l'univers au
// moment où le fait s'est produit ; le handler ne fait plus que recopier.

public sealed record PaymentInitiatedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, Guid BuyerId,
    decimal Amount, string Currency, string Provider) : DomainEvent;

/// <summary>Le paiement a été encaissé — déclenche la confirmation de la commande.
/// Porte provider/méthode/montant/devise pour les métriques (aucune donnée perso).</summary>
public sealed record PaymentCapturedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Provider, string Method,
    decimal Amount, string Currency) : DomainEvent;

/// <summary>Le paiement a échoué — déclenche l'annulation de la commande.</summary>
public sealed record PaymentFailedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Reason, string Provider,
    string Method, string Currency) : DomainEvent;

/// <summary>
/// Le paiement a été remboursé.
/// </summary>
/// <remarks>
/// PORTE LE <c>BuyerId</c>, ET C'EST NÉCESSAIRE.
///
/// L'événement d'intégration qui en découle n'avait aucun consommateur :
/// l'argent revenait — quand il revenait — sans que l'acheteur en soit jamais
/// informé. Le prévenir suppose de savoir qui il est, et la commande seule ne
/// suffit pas : communication-service devrait alors interroger order-service
/// pour chaque remboursement, pour une donnée que Payments a déjà sous la main.
/// </remarks>
public sealed record PaymentRefundedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, Guid BuyerId, string Provider,
    decimal Amount, string Currency, Guid RefundId, Guid? ReturnId, Guid? ExternalRefundId,
    string IdempotencyKey, string ProviderRefundId) : DomainEvent;

public sealed record PaymentRefundFailedDomainEvent(
    Guid PaymentId, Guid OrderId, string OrderType, string Provider,
    decimal Amount, string Currency, Guid RefundId, Guid? ReturnId, Guid? ExternalRefundId,
    string IdempotencyKey, string Reason) : DomainEvent;
