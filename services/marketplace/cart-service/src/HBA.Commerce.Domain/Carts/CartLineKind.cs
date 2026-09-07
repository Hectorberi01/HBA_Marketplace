namespace HBA.Commerce.Domain.Carts;

/// <summary>CE QU'UNE LIGNE DE PANIER REPRÉSENTE.</summary>
public enum CartLineKind
{
    /// <summary>Une offre marketplace : SKU, stock, expédition.</summary>
    Goods = 0,

    /// <summary>Un plat de restaurant : options, préparation en cuisine.</summary>
    Food = 1
}
