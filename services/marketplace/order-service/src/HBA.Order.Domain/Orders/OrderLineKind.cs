namespace HBA.Orders.Domain.Orders;

/// <summary>CE QU'UNE LIGNE DE COMMANDE REPRÉSENTE.</summary>
public enum OrderLineKind
{
    /// <summary>Une offre marketplace : stock réservé, expédition.</summary>
    Goods = 0,

    /// <summary>Un plat de restaurant : préparation en cuisine, pas de stock.</summary>
    Food = 1
}
