namespace HBA.Financial.Payments.Domain.PaymentMethods;

/// <summary>Identifiant fort d'un moyen de paiement enregistré.</summary>
public readonly record struct SavedPaymentMethodId(Guid Value)
{
    public static SavedPaymentMethodId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
