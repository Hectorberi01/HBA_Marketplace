namespace HBA.Promotions.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA FORME PUBLIQUE DES ÉNUMÉRATIONS DE PROMOTION.
///
/// LE CONTRAT PUBLIC N'EST PAS `Enum.ToString()`.
///
/// Le §10.16 écrit `FOOD|MARKETPLACE|GLOBAL` et `PERCENT|FIXED|FREE_DELIVERY`.
/// Les énumérations C# rendent « Food » et « FreeDelivery ». Laisser fuiter la
/// casse du langage ferait dépendre un contrat public de la façon dont ces
/// valeurs sont ÉCRITES dans le code : un renommage purement cosmétique —
/// `FreeDelivery` en `FreeShipping`, une majuscule déplacée — casserait des
/// consommateurs qui n'ont rien demandé.
///
/// La conversion vit ICI, dans le projet de contrat, et non dans le gestionnaire
/// qui publie. C'est ce qui la rend vérifiable depuis l'extérieur : enfouie dans
/// une classe interne de la couche application, elle n'aurait été testable que
/// par l'événement produit, donc jamais sur les cas limites.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PromotionConstantes
{
    /// <summary>« FreeDelivery » → « FREE_DELIVERY ». « Food » → « FOOD ».</summary>
    public static string Convertir(string? valeurCSharp)
    {
        if (string.IsNullOrWhiteSpace(valeurCSharp))
        {
            return string.Empty;
        }

        var texte = valeurCSharp.Trim();

        return string.Concat(texte.Select((c, i) =>
            char.IsUpper(c) && i > 0 ? "_" + c : c.ToString())).ToUpperInvariant();
    }
}
