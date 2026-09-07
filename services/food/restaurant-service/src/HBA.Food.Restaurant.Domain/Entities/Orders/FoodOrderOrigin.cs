namespace HBA.Food.Domain.Orders;

/// <summary>DE QUEL UNIVERS VIENT LA COMMANDE QUE CE TICKET PRÉPARE.</summary>
public enum FoodOrderOrigin
{
    /// <summary>Une commande d'order-service (`OrderLineKind.Food`).</summary>
    Marketplace = 0,

    /// <summary>Une `MealOrder` de food-order-service.</summary>
    Food = 1
}
