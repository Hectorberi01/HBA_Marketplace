namespace HBA.Delivery.Dispatch.Domain.ValueObjects;

public sealed record DispatchRequirements(
    string? VehicleRequirement,
    int Priority,
    decimal? MaxPackageWeightKg);
