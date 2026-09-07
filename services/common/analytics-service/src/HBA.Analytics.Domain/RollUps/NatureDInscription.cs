namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce qui s'est inscrit : un acheteur, ou un vendeur.
/// </summary>
/// <remarks>
/// LES DEUX NE SONT PAS EXCLUSIFS, ET LE GRAPHE NE DOIT PAS LE LAISSER CROIRE.
///
/// Un vendeur s'inscrit d'abord comme UTILISATEUR — `UserRegistered` — puis ouvre
/// un dossier vendeur — `SellerRegistered`. Il compte donc dans les deux séries,
/// le même jour ou plus tard. « Acheteurs + vendeurs » n'est pas le nombre de
/// comptes créés, et additionner les deux courbes surcompte exactement les
/// vendeurs.
///
/// Séparer les deux à la source demanderait à identity-service de savoir, au
/// moment de l'inscription, ce que ce compte deviendra — ce qu'il ne sait pas et
/// n'a pas à savoir.
/// </remarks>
public static class NatureDInscription
{
    public const string Acheteur = "Buyer";
    public const string Vendeur = "Seller";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;
}
