namespace HBA.Delivery.Proof.Domain.Policies;

public static class ProofRequirementPolicy
{
    public static bool RequiresDropoffProof(string deliveryType) =>
        !string.Equals(deliveryType, "DIGITAL", StringComparison.OrdinalIgnoreCase);
}
