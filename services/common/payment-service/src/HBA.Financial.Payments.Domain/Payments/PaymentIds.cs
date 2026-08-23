namespace HBA.Financial.Payments.Domain.Payments;

/// <summary>Identité forte d'un paiement.</summary>
public readonly record struct PaymentId(Guid Value)
{
    public static PaymentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Moyen de paiement (Mobile Money en priorité sur le marché visé).</summary>
public enum PaymentMethod
{
    MobileMoney = 0,
    Card = 1,
    BankTransfer = 2,
    CashOnDelivery = 3
}

/// <summary>
/// Mode d'interaction avec le prestataire (PSP) :
/// - HostedCheckout : page de paiement hébergée par le PSP (redirection).
/// - PaymentIntent : intention créée côté serveur, confirmée côté client (SDK).
/// </summary>
public enum PaymentFlow
{
    HostedCheckout = 0,
    PaymentIntent = 1
}

/// <summary>Statut du paiement, piloté par le Saga de commande.</summary>
/// <summary>
/// Univers métier de la commande payée (§10.12, colonne <c>order_type</c>).
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CETTE COLONNE, `OrderId` EST AMBIGU ENTRE DEUX SERVICES.
///
/// `marketplace-order-service` et `food-order-service` tiennent chacun leurs
/// commandes, dans leur propre base, avec leurs propres identifiants. Un
/// `payment.succeeded` qui ne porte que `OrderId` oblige donc les DEUX à chercher
/// cet identifiant chez eux, et celui qui ne le trouve pas ne peut pas distinguer
/// « ce paiement n'est pas pour moi » de « ma commande a disparu ».
///
/// Aujourd'hui le problème est latent : seul le Marketplace consomme l'événement.
/// Il devient réel au premier paiement Food — c'est-à-dire au moment où il coûtera
/// le plus cher à diagnostiquer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum PaymentOrderType
{
    /// <summary>
    /// Valeur zéro, donc valeur des lignes déjà en base après migration. C'est
    /// exact : tous les paiements existants sont des commandes Marketplace, le
    /// Food n'ayant pas encore de chemin de paiement.
    /// </summary>
    Marketplace = 0,

    Food = 1
}

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Captured = 2,
    Failed = 3,
    Refunded = 4
}

public enum PaymentRefundStatus
{
    Processing = 0,
    Succeeded = 1,
    Failed = 2
}
