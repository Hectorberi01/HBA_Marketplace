namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>QUELLE PREUVE EXIGER À LA REMISE — DÉCIDÉ ICI, JAMAIS PAR L'APPELANT.</summary>
public static class ProofPolicy
{
    /// <summary>
    /// Au-delà de ce montant (FCFA), la remise exige un code dicté par le client.
    /// </summary>
    public const decimal HighValueThreshold = 50_000m;

    /// <summary>La preuve exigée à la remise, d'après ce que la course transporte.</summary>
    /// <param name="declaredValue">
    /// Valeur des marchandises déclarée par le donneur d'ordre, en FCFA. Nulle
    /// quand il ne la connaît pas — elle est alors traitée comme faible, ce qui est
    /// le choix PRUDENT côté friction et le choix RISQUÉ côté litige.
    /// </param>
    /// <param name="isCashOnDelivery">
    /// La course encaisse-t-elle le prix des marchandises à la remise ?
    /// </param>
    public static ProofOfDeliveryKind RequiredFor(decimal? declaredValue, bool isCashOnDelivery)
    {
        if (isCashOnDelivery)
        {
            return ProofOfDeliveryKind.Pin;
        }

        if (declaredValue is { } valeur && valeur >= HighValueThreshold)
        {
            return ProofOfDeliveryKind.Pin;
        }

        return ProofOfDeliveryKind.Photo;
    }
}
