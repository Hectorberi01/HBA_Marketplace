namespace HBA.Analytics.Domain.RollUps;

/// <summary>Ce qui s'est inscrit : un acheteur, un vendeur, ou un livreur.</summary>
public static class NatureDInscription
{
    public const string Acheteur = "Buyer";
    public const string Vendeur = "Seller";
    public const string Livreur = "Driver";

    /// <summary>Longueur retenue en base pour cette colonne.</summary>
    public const int LongueurMax = 16;
}
