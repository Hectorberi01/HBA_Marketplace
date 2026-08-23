namespace HBA.Delivery.Proof.Domain.Entities;

public sealed record ProofVerification(
    Guid Id,
    Guid ProofId,
    string Status,
    string? Reason,
    DateTimeOffset VerifiedAt);
