namespace HBA.Deliveries.Domain.Deliveries;

public static class CancellationPolicy
{
    public static bool CanCancel(DeliveryStatus status) => !DeliveryStateMachine.IsTerminal(status);
}
