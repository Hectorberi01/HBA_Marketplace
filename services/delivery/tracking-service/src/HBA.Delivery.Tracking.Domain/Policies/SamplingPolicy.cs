namespace HBA.Delivery.Tracking.Domain.Policies;

public static class SamplingPolicy
{
    public static bool AcceptSequence(long lastSequence, long candidateSequence) =>
        candidateSequence > lastSequence;
}
