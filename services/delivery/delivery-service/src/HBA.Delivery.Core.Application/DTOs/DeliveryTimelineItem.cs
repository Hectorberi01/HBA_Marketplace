namespace HBA.Deliveries.Application.Deliveries.Queries;

public sealed record DeliveryTimelineItem(string Status, DateTime OccurredAtUtc, string? Note = null);
