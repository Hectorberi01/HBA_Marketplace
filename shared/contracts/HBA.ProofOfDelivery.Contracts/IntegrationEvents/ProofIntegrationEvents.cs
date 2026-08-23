using HBA.Shared.IntegrationEvents;

namespace HBA.ProofOfDelivery.Contracts.IntegrationEvents;

[HbaEvent("proof.submitted", Version = 1, AggregateType = "DeliveryProof")]
public sealed record ProofSubmittedIntegrationEvent : IntegrationEvent
{
    public required Guid ProofId { get; init; }
    public required Guid DeliveryId { get; init; }
}

[HbaEvent("proof.verified", Version = 1, AggregateType = "DeliveryProof")]
public sealed record ProofVerifiedIntegrationEvent : IntegrationEvent
{
    public required Guid ProofId { get; init; }
    public required Guid DeliveryId { get; init; }
}

[HbaEvent("proof.rejected", Version = 1, AggregateType = "DeliveryProof")]
public sealed record ProofRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ProofId { get; init; }
    public required Guid DeliveryId { get; init; }
    public required string Reason { get; init; }
}

[HbaEvent("delivery.proof-completed", Version = 1, AggregateType = "Delivery")]
public sealed record DeliveryProofCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryId { get; init; }
    public required Guid ProofId { get; init; }
}
