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
/// Mode d'interaction avec le prestataire (PSP) : - HostedCheckout : page de
/// paiement hébergée par le PSP (redirection).
/// </summary>
public enum PaymentFlow
{
    HostedCheckout = 0,
    PaymentIntent = 1
}

/// <summary>Statut du paiement, piloté par le Saga de commande.</summary>
/// <summary>Univers métier de la commande payée (§10.12, colonne <c>order_type</c>).</summary>
public enum PaymentOrderType
{
    /// <summary>Valeur zéro, donc valeur des lignes déjà en base après migration.</summary>
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
