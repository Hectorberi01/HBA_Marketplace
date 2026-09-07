namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce que la plateforme a vendu, un jour donné, par nature de commande et par
/// devise.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS LA SOMME DES LIGNES VENDEUR, ET LA DIFFÉRENCE COMPTE.
///
/// Une commande de REPAS n'a aucune part vendeur — `BuildSellerShares` l'écarte
/// délibérément, c'est écrit dans le contrat. Elle ne produit donc AUCUNE ligne
/// vendeur, et elle produit bien une ligne ici. Totaliser `seller_daily` pour
/// obtenir le volume de la plateforme oublierait toute la restauration.
///
/// LE « GMV » DE CETTE TABLE EST UNE SOMME DE PARTS VENDEUR, ET DONC INCOMPLET.
///
/// `OrderConfirmedIntegrationEvent` ne porte pas le total payé par l'acheteur :
/// il porte la répartition par vendeur. Ce que cette colonne additionne est donc
/// la marchandise, hors frais de livraison et hors commission — et, pour une
/// commande de repas, ZÉRO, faute de parts.
///
/// C'EST UNE LIMITE DU CONTRAT, PAS DE CE SERVICE, et elle se lève d'une ligne :
/// un champ `GrandTotal` optionnel sur l'événement, comme le lot 2 en ajoute
/// trois autres. Tant qu'il n'existe pas, `Gmv` vaut ce qu'il vaut et le graphe
/// doit le dire — « volume marchand », pas « chiffre d'affaires de la
/// plateforme ». `OrdersCount` et `ItemsCount`, eux, sont exacts.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ActiviteJournalierePlateforme
{
    public DateOnly Day { get; init; }

    public string Kind { get; init; } = default!;

    public string Currency { get; init; } = default!;

    /// <summary>Commandes confirmées. Exact, quelle que soit la nature.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Articles vendus. Vaut zéro pour une commande de repas — voir l'encadré.</summary>
    public int ItemsCount { get; set; }

    /// <summary>Volume marchand : somme des parts vendeur. Voir l'encadré.</summary>
    public decimal Gmv { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une commande confirmée à la journée.</summary>
    public void Ajouter(int articles, decimal volume, DateTime instantUtc)
    {
        OrdersCount += 1;
        ItemsCount += articles;
        Gmv += volume;
        UpdatedAtUtc = instantUtc;
    }
}
