using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Contracts.IntegrationEvents;

/// <summary>LES ÉVÉNEMENTS DE COMMANDE ET DE CUISINE, HORS DU MODULE (cahier §19).</summary>
public static class FoodOrderOrigins
{
    /// <summary>DE QUEL UNIVERS VIENT L'<c>OrderId</c> PORTÉ PAR CES ÉVÉNEMENTS.</summary>
    public const string Marketplace = "Marketplace";

    public const string Food = "Food";
}
[HbaEvent("food.order.received")]
public sealed record FoodOrderReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required decimal Total { get; init; }
    public required int ItemCount { get; init; }

    /// <summary>
    /// L'univers de <see cref="FoodOrderReceivedIntegrationEvent.OrderId"/> —
    /// <see cref="FoodOrderOrigins"/> .
    /// </summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>Le restaurant a accepté. Vaut aussi <c>kitchen.ticket.created</c>.</summary>
[HbaEvent("food.order.accepted")]
public sealed record FoodOrderAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required int EstimatedPreparationMinutes { get; init; }

    /// <summary>NUL quand l'acceptation est AUTOMATIQUE (§3).</summary>
    public Guid? AcceptedByUserId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>Le restaurant a refusé.</summary>
[HbaEvent("food.order.rejected")]
public sealed record FoodOrderRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required string Reason { get; init; }
    public string? Comment { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>La cuisine a commencé. Vaut <c>kitchen.ticket.started</c>.</summary>
[HbaEvent("food.order.preparing")]
public sealed record FoodOrderPreparingIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>LE SAC EST PRÊT — L'ÉVÉNEMENT QUI APPELLE UN LIVREUR.</summary>
[HbaEvent("food.order.ready.for.pickup")]
public sealed record FoodOrderReadyForPickupIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public required DateTime ReadyAtUtc { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

[HbaEvent("food.order.picked.up")]
public sealed record FoodOrderPickedUpIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>LE REPAS EST REMIS AU CLIENT — L'ÉVÉNEMENT QUI FAIT PAYER LE RESTAURATEUR.</summary>
[HbaEvent("food.order.delivered")]
public sealed record FoodOrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}

/// <summary>Annulée.</summary>
[HbaEvent("food.order.cancelled")]
public sealed record FoodOrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid FoodOrderId { get; init; }
    public required Guid OrderId { get; init; }
    public required Guid RestaurantId { get; init; }
    public string? Reason { get; init; }
    public required bool WasInKitchen { get; init; }

    /// <summary>L'univers de <c>OrderId</c> — voir <see cref="FoodOrderOrigins"/>.</summary>
    public string OrderOrigin { get; init; } = FoodOrderOrigins.Marketplace;
}
