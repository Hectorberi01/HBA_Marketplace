using HBA.Shared.Domain.Primitives;

namespace HBA.Marketplace.ReturnRefund.Domain.Aggregates.ReturnRequest;

public sealed class ReturnShipment : Entity<Guid>
{
    private ReturnShipment()
    {
    }

    public ReturnShipment(Guid id, Guid returnId, string deliveryId, string mode, string? trackingNumber, DateTime createdAtUtc)
        : base(id)
    {
        ReturnId = returnId;
        DeliveryId = deliveryId;
        Mode = mode;
        TrackingNumber = trackingNumber;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ReturnId { get; private set; }
    public string DeliveryId { get; private set; } = string.Empty;
    public string Mode { get; private set; } = string.Empty;
    public string? TrackingNumber { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
