using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Contracts.IntegrationEvents;

/// <summary>HBA A VALIDÉ UN ÉTABLISSEMENT.</summary>
[HbaEvent("food.restaurant.approved")]
public sealed record RestaurantApprovedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }

    /// <summary>Le compte HBA du restaurateur — celui qui reçoit le rôle.</summary>
    public required Guid OwnerUserId { get; init; }

    public required string Name { get; init; }
}

/// <summary>Le dossier a été REFUSÉ par la modération.</summary>
[HbaEvent("food.restaurant.rejected")]
public sealed record RestaurantRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>L'établissement a été SUSPENDU par la plateforme : il quitte la vitrine.</summary>
[HbaEvent("food.restaurant.suspended")]
public sealed record RestaurantSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
    public string? Reason { get; init; }
}

/// <summary>La suspension est levée : l'établissement revient dans la vitrine.</summary>
[HbaEvent("food.restaurant.reopened")]
public sealed record RestaurantReopenedIntegrationEvent : IntegrationEvent
{
    public required Guid RestaurantId { get; init; }
    public required Guid OwnerUserId { get; init; }
}
