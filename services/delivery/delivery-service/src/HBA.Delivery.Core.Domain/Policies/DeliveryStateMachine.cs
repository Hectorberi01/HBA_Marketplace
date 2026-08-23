namespace HBA.Deliveries.Domain.Deliveries;

public static class DeliveryStateMachine
{
    public static bool IsTerminal(DeliveryStatus status) =>
        status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled;
}
