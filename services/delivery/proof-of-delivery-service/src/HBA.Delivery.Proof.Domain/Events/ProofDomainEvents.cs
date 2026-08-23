namespace HBA.Delivery.Proof.Domain.Events;

public sealed record ProofCreatedDomainEvent(Guid ProofId, Guid DeliveryId);

public sealed record ProofSubmittedDomainEvent(Guid ProofId, Guid DeliveryId);

public sealed record ProofVerifiedDomainEvent(Guid ProofId, Guid DeliveryId);

public sealed record ProofRejectedDomainEvent(Guid ProofId, Guid DeliveryId, string Reason);
