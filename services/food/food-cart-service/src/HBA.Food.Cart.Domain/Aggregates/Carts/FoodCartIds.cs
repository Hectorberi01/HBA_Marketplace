namespace HBA.FoodCarts.Domain.Carts;

/// <summary>Identité forte d'un panier de restauration.</summary>
public readonly record struct FoodCartId(Guid Value)
{
    public static FoodCartId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut du panier.</summary>
public enum FoodCartStatus
{
    Active = 0,
    CheckedOut = 1,

    /// <summary>
    /// ÉTAT INATTEIGNABLE, comme <c>CartStatus.Abandoned</c> côté marchandise
    /// (lot 9.2) : aucun balayeur ne pose cette valeur, et
    /// `ux_food_carts_active_buyer` impose un seul panier repas actif par
    /// acheteur. Conservée pour la même raison — c'est le vocabulaire du
    /// balayeur à venir, pas du bruit à supprimer.
    /// </summary>
    Abandoned = 2
}
