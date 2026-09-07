using HBA.Shared.IntegrationEvents;

namespace HBA.Financial.Wallet.Contracts.IntegrationEvents;

/// <summary>Un reversement a été versé à un vendeur.</summary>
[HbaEvent("payout.completed", Version = 1, AggregateType = "Payout")]
public sealed record PayoutPaidIntegrationEvent : IntegrationEvent
{
    public required Guid BatchId { get; init; }
    public required Guid PayoutId { get; init; }
    public required Guid SellerId { get; init; }
    public required decimal NetAmount { get; init; }
    public required string Currency { get; init; }
}

/// <summary>Le gain d'une course est ARRIVÉ au portefeuille du livreur.</summary>
[HbaEvent("wallet.credited", Version = 1, AggregateType = "Wallet")]
public sealed record DriverEarningCreditedIntegrationEvent : IntegrationEvent
{
    public required Guid DriverId { get; init; }
    public required Guid DeliveryId { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
}
