namespace HBA.FoodOrders.Contracts;

/// <summary>Une option figée sur une ligne de commande.</summary>
public sealed record MealOrderLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>Une ligne de commande de repas, FIGÉE.</summary>
public sealed record MealOrderLineSummary(
    Guid LineId,
    Guid MenuItemId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency,
    string? Notes,
    IReadOnlyList<MealOrderLineOptionSummary> Options);

/// <summary>OÙ LE REPAS DOIT ÊTRE PORTÉ.</summary>
public sealed record MealOrderShippingAddressSummary(
    string? Recipient,
    string? Phone,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude);

/// <summary>Une commande de repas, vue de l'extérieur du service.</summary>
/// <param name="ShippingAddress">L'adresse de remise figée à la commande.</param>
public sealed record MealOrderSummary(
    Guid OrderId,
    Guid BuyerId,
    Guid RestaurantId,
    string Status,
    decimal Subtotal,
    decimal ShippingFee,
    decimal TotalAmount,
    string Currency,
    string? PromotionCode,
    string? DeliveryQuoteId,
    string? CustomerNote,
    DateTime CreatedOnUtc,
    IReadOnlyList<MealOrderLineSummary> Lines,
    MealOrderShippingAddressSummary? ShippingAddress = null);

/// <summary>API publique du service de commande de repas.</summary>
public interface IMealOrderModuleApi
{
    Task<MealOrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Cet acheteur a-t-il déjà commandé un repas ?</summary>
    Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default);
}
