using HBA.Shared.IntegrationEvents;

namespace HBA.Deliveries.Contracts.IntegrationEvents;

/// <summary>LES FAITS QUE DELIVERIES PUBLIE AU RESTE DU SYSTÈME.</summary>
[HbaEvent("delivery.created")]
public sealed record DeliveryCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required string Type { get; init; }
}

/// <summary>
/// Une course est PROPOSÉE à un livreur, qui a quarante-cinq secondes pour
/// répondre.
/// </summary>
[HbaEvent("delivery.assigned")]
public sealed record DeliveryAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid DriverId { get; init; }
}

// `DriverVerifiedIntegrationEvent` A ÉTÉ RETIRÉ D'ICI. NE PAS LE REMETTRE.

/// <summary>Un livreur a accepté la course.</summary>
[HbaEvent("delivery.accepted")]
public sealed record DeliveryAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
}

/// <summary>Le colis est pris en charge : la course est physiquement engagée.</summary>
[HbaEvent("delivery.picked.up")]
public sealed record DeliveryPickedUpIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }

    /// <summary>
    /// Code de remise à porter au DESTINATAIRE, chiffré (AES-GCM,
    /// `ISecretProtector`).
    /// </summary>
    public string? ProtectedDeliveryPin { get; init; }
}

/// <summary>Remise effectuée.</summary>
[HbaEvent("delivery.completed")]
public sealed record DeliveryCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required Guid DriverId { get; init; }
    public required DateTime DeliveredAtUtc { get; init; }

    /// <summary>Part revenant au livreur, figée à la remise.</summary>
    public decimal? DriverEarning { get; init; }

    public string? Currency { get; init; }
}

/// <summary>Course annulée avant collecte.</summary>
[HbaEvent("delivery.cancelled")]
public sealed record DeliveryCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Aucun livreur trouvé après épuisement des tentatives.</summary>
[HbaEvent("delivery.no.driver.available")]
public sealed record DeliveryNoDriverAvailableIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required string Reference { get; init; }
    public required string Source { get; init; }
    public required int Attempts { get; init; }
}
