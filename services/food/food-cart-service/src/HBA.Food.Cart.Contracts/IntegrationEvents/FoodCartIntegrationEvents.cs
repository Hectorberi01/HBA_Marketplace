using HBA.Shared.IntegrationEvents;

namespace HBA.FoodCarts.Contracts.IntegrationEvents;

/// <summary>
/// Le panier de restauration a été clos parce que la commande est partie.
///
/// ÉMIS PAR LE PANIER, PAS PAR LA COMMANDE — et il n'est pas le déclencheur.
///
/// C'est food-order-service qui annonce `MealOrderPlaced` ; le panier l'écoute,
/// se clôt, et publie ceci pour l'analytique. L'ordre compte : si le panier
/// publiait d'abord et que la commande échouait, on aurait un panier vidé sans
/// commande, et un client dont le repas a disparu de l'écran.
/// </summary>
public sealed record FoodCartCheckedOutIntegrationEvent : IntegrationEvent
{
    public required Guid CartId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid RestaurantId { get; init; }
}
