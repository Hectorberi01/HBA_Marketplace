namespace HBA.Deliveries.Domain.Deliveries;

public enum DeliveryStatus
{
    Pending = 0,
    SearchingDriver = 1,
    DriverAssigned = 2,
    DriverAccepted = 3,
    ArrivedAtPickup = 4,
    PickedUp = 5,
    InTransit = 6,
    ArrivedAtDropoff = 7,
    Delivered = 8,
    Cancelled = 9,
    NoDriverAvailable = 10
}
