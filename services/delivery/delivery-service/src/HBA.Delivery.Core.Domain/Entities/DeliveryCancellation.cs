namespace HBA.Deliveries.Domain.Deliveries;

public sealed record DeliveryCancellation(string? Reason, DateTime CancelledAtUtc);
