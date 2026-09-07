namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// La nature d'une commande telle que ce service la range : marchandise ou repas.
/// </summary>
/// <remarks>
/// C'EST UNE CHAÎNE, PARCE QUE L'ÉVÉNEMENT EN PORTE UNE.
///
/// `OrderConfirmedIntegrationEvent.Kind` vaut « Goods » ou « Food », et son défaut
/// est « Goods » pour les messages émis avant l'ajout du champ. Une énumération
/// locale devrait décider quoi faire d'une troisième valeur inconnue — et le seul
/// choix honnête serait de la garder telle quelle pour ne pas la ranger sous une
/// nature qui n'est pas la sienne. Autant conserver la chaîne du contrat.
///
/// LA NORMALISATION EXISTE POUR QUE « goods » ET « Goods » NE FASSENT PAS DEUX
/// LIGNES. Elles sont dans la clé primaire des deux tables de roll-up : une
/// casse différente y produirait deux séries pour une seule réalité, et personne
/// ne le verrait avant de comparer deux totaux qui ne se rejoignent pas.
/// </remarks>
public static class NatureDeCommande
{
    public const string Marchandise = "Goods";
    public const string Repas = "Food";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;

    /// <summary>
    /// Range une valeur venue du fil. Une nature inconnue est CONSERVÉE telle
    /// quelle, pas repliée sur « Goods » : la replier ferait passer pour de la
    /// marchandise ce que le producteur a appelé autrement.
    /// </summary>
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
