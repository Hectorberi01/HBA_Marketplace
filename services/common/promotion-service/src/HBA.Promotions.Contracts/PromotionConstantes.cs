namespace HBA.Promotions.Contracts;

/// <summary>LA FORME PUBLIQUE DES ÉNUMÉRATIONS DE PROMOTION.</summary>
public static class PromotionConstantes
{
    /// <summary>« FreeDelivery » → « FREE_DELIVERY ».</summary>
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
