namespace HBA.Analytics.Domain.RollUps;

/// <summary>Ce qu'une tentative de paiement est devenue, et par qui elle est passée.</summary>
public static class IssueDePaiement
{
    public const string Encaisse = "Captured";
    public const string Echoue = "Failed";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;
}

/// <summary>Le prestataire de paiement, tel que la table le range.</summary>
public static class FournisseurDePaiement
{
    /// <summary>Le seau des messages qui ne portaient pas encore le champ.</summary>
    public const string Inconnu = "inconnu";

    /// <summary>La devise des messages qui ne la portaient pas encore.</summary>
    public const string DeviseInconnue = "XXX";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 40;

    /// <summary>Range un nom de prestataire venu du fil.</summary>
    public static string Normaliser(string? duFil)
    {
        if (string.IsNullOrWhiteSpace(duFil))
        {
            return Inconnu;
        }

        var nettoye = duFil.Trim().ToLowerInvariant();
        return nettoye.Length <= LongueurMax ? nettoye : nettoye[..LongueurMax];
    }

    /// <summary>Range une devise venue du fil.</summary>
    public static string NormaliserLaDevise(string? duFil)
        => string.IsNullOrWhiteSpace(duFil) ? DeviseInconnue : duFil.Trim().ToUpperInvariant();
}
