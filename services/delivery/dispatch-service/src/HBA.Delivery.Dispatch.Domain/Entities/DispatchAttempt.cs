namespace HBA.Delivery.Dispatch.Domain.Entities;

public sealed record DispatchAttempt(
    Guid Id,
    Guid DispatchJobId,
    int AttemptNumber,
    int SearchRadiusMeters,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status);
