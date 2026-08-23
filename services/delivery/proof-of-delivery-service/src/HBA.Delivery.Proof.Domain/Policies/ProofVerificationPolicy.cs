using HBA.Delivery.Proof.Domain.Entities;

namespace HBA.Delivery.Proof.Domain.Policies;

public static class ProofVerificationPolicy
{
    public static bool CanVerify(bool otpVerified, IReadOnlyList<ProofMedia> media) =>
        otpVerified || media.Count > 0;
}
