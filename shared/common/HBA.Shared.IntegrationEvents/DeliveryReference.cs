namespace HBA.Shared.IntegrationEvents;

/// <summary>La convention de référence qui relie une course à ce qu'elle transporte.</summary>
public static class DeliveryReference
{
    /// <summary>Une commande marketplace, sans expédition intermédiaire.</summary>
    public const string OrderPrefix = "ORDER-";

    /// <summary>
    /// Un ticket de cuisine — la référence est celle du TICKET, pas de la commande.
    /// </summary>
    public const string FoodPrefix = "FOOD-";

    /// <summary>Une expédition du module Shipping, resté dans le monolithe.</summary>
    public const string ShipmentPrefix = "SHIP-";

    public static string ForOrder(Guid orderId) => Build(OrderPrefix, orderId);

    public static string ForFoodOrder(Guid foodOrderId) => Build(FoodPrefix, foodOrderId);

    /// <summary>Relit une référence pour un préfixe donné.</summary>
    public static Guid? Read(string? reference, string prefix)
        => reference is not null
           && reference.StartsWith(prefix, StringComparison.Ordinal)
           && Guid.TryParseExact(reference[prefix.Length..], "N", out var parsed)
            ? parsed
            : null;

    public static Guid? ReadOrder(string? reference) => Read(reference, OrderPrefix);

    public static Guid? ReadFoodOrder(string? reference) => Read(reference, FoodPrefix);

    // FORMAT « N » — TRENTE-DEUX CARACTÈRES SANS TIRETS.
    private static string Build(string prefix, Guid id) => $"{prefix}{id:N}";
}
