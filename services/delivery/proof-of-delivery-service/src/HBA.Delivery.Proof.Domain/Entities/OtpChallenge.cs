namespace HBA.Delivery.Proof.Domain.Entities;

public sealed record OtpChallenge(
    Guid Id,
    Guid ProofId,
    string OtpHash,
    DateTimeOffset ExpiresAt,
    int Attempts,
    bool Verified);
