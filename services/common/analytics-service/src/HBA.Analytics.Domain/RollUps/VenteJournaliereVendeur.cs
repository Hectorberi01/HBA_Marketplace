namespace HBA.Analytics.Domain.RollUps;

/// <summary>
/// Ce qu'un vendeur a vendu, un jour donné, dans une devise et pour une nature de
/// commande.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA CLÉ PORTE LA DEVISE ET LA NATURE, ET LES DEUX SONT INDISPENSABLES.
///
/// LA DEVISE : additionner des francs CFA et des euros dans une colonne
/// « chiffre d'affaires » produit un nombre qui n'est pas une somme d'argent. Le
/// dépôt n'a qu'une devise en production aujourd'hui ; le jour où il en aura
/// deux, une table sans devise serait déjà fausse et personne ne saurait quelles
/// lignes réparer.
///
/// LA NATURE : c'est elle qui rend « part marchandise / repas » gratuite. La
/// porter en COLONNES — deux compteurs par ligne — obligerait à ajouter une
/// colonne à chaque nouvelle nature, donc une migration ; en clé, une nature
/// nouvelle crée une ligne et rien d'autre.
///
/// CE QUE CETTE TABLE NE PORTE PAS, ET C'EST VOULU : aucun identifiant de
/// commande. Une ligne agrège, elle ne trace pas. Qui veut la commande la lit
/// chez son propriétaire — order-service — et n'a rien à faire ici.
///
/// LE MONTANT EST LA PART DU VENDEUR, PAS LE TOTAL PAYÉ PAR L'ACHETEUR.
///
/// `OrderSellerShare.Amount` est la somme des `LineTotal` des lignes de ce
/// vendeur — le prix FINAL, REMISES COMPRISES, qu'elles soient portées par le
/// vendeur ou par la plateforme. Ce qui n'y est pas : les frais de livraison, qui
/// n'appartiennent à aucun vendeur, et la commission de la plateforme, qui se
/// retire plus loin, dans Settlement.
///
/// CETTE PRÉCISION A ÉTÉ CORRIGÉE APRÈS COUP, et l'erreur valait d'être relevée :
/// la version précédente de cet encadré écrivait que les remises plateforme
/// « n'y sont pas ». C'est faux — `LineTotal` est le prix après remises. La
/// vérification tient en une lecture de `Order.BuildSellerShares`, qui dit
/// exactement la même chose que `OrderMapper.ToSellerSummary` : les deux
/// somment `LineTotal` sur les lignes du vendeur. Les deux chiffres coïncident
/// donc, et c'est ce qui rend le remplacement du calcul de la passerelle sûr.
///
/// Un vendeur qui compare ce chiffre à son relevé de versement trouvera un
/// écart, et l'écart est exactement la commission : c'est le CHIFFRE D'AFFAIRES,
/// pas le gain net. Le gain net appartient à payment-service, qui tient le
/// portefeuille.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class VenteJournaliereVendeur
{
    public Guid SellerId { get; init; }

    public DateOnly Day { get; init; }

    public string Currency { get; init; } = default!;

    public string Kind { get; init; } = default!;

    /// <summary>Nombre de commandes confirmées où ce vendeur a une part.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Nombre d'articles vendus, toutes lignes confondues.</summary>
    public int ItemsCount { get; set; }

    /// <summary>Somme des parts vendeur. Voir l'encadré : ce n'est pas le gain net.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Dernière écriture. Sert au diagnostic d'un rattrapage, pas au calcul.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Ajoute une part de commande à la journée.</summary>
    public void Ajouter(int articles, decimal montant, DateTime instantUtc)
    {
        OrdersCount += 1;
        ItemsCount += articles;
        Revenue += montant;
        UpdatedAtUtc = instantUtc;
    }
}
