namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QU'UNE LIGNE DE PANIER REPRÉSENTE.
///
/// CE N'EST PAS UNE CATÉGORIE DE PRODUIT — C'EST UN CHEMIN D'EXÉCUTION.
///
/// Une ligne <see cref="Goods"/> désigne une offre : elle a un SKU, du stock à
/// réserver dans Inventory, un lieu d'expédition, et part en colis. Une ligne
/// <see cref="Food"/> désigne un plat : elle n'a ni SKU ni stock, elle porte des
/// options choisies, et elle part en cuisine avant d'être livrée chaude.
///
/// Les deux ne peuvent pas emprunter la même chaîne. `PlaceOrder` réserve du
/// stock par SKU : appliquer cela à un plat échouerait sur un article qui n'existe
/// pas dans Inventory, et l'échec se produirait APRÈS le paiement. C'est ce
/// discriminant qui permet de sauter l'étape au bon endroit plutôt que de la faire
/// échouer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum CartLineKind
{
    /// <summary>Une offre marketplace : SKU, stock, expédition.</summary>
    Goods = 0,

    /// <summary>Un plat de restaurant : options, préparation en cuisine.</summary>
    Food = 1
}
