namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// La nature d'une commande telle que ce service la range : marchandise ou repas.
/// </summary>
public static class NatureDeCommande
{
    public const string Marchandise = "Goods";
    public const string Repas = "Food";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;

    /// <summary>Range une valeur venue du fil.</summary>
    public static string Normaliser(string? valeurDuFil)
    {
        if (string.IsNullOrWhiteSpace(valeurDuFil))
        {
            return Marchandise;
        }

        var nettoyee = valeurDuFil.Trim();

        if (string.Equals(nettoyee, Marchandise, StringComparison.OrdinalIgnoreCase))
        {
            return Marchandise;
        }

        if (string.Equals(nettoyee, Repas, StringComparison.OrdinalIgnoreCase))
        {
            return Repas;
        }

        return nettoyee.Length <= LongueurMax ? nettoyee : nettoyee[..LongueurMax];
    }
}
