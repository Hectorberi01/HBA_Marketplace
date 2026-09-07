namespace HBA.Gateway.Application.Contracts.Delivery;

/// <summary>Compte du livreur connecté — miroir de <c>DriverAccountView</c>.</summary>
public sealed record DriverAccount(
    Guid DriverId,
    Guid UserId,
    string FullName,
    string Phone,
    string Vehicle,
    string AccountStatus,
    string Availability,
    string? StatusReason,
    int CompletedDeliveries,
    DateTime RegisteredAtUtc,
    DateTime? VerifiedAtUtc);

/// <summary>Une mission telle que le livreur la voit — miroir de <c>MyDeliveryDto</c>.</summary>
public sealed record DriverMission(
    Guid DeliveryId,
    string Reference,
    string Status,
    string Type,
    DriverStop Pickup,
    DriverStop Dropoff,
    string? PackageDescription,
    decimal? PackageWeightKg,
    bool IsFragile,
    string RequiredProof,
    decimal? Price,
    decimal? EstimatedEarning,
    string? Currency,
    DateTime? ScheduledForUtc,
    DateTime? OfferedAtUtc,
    DateTime? OfferExpiresAtUtc);

/// <summary>Un point de la course.</summary>
public sealed record DriverStop(
    string ContactName,
    string Phone,
    string CommuneName,
    string? Quartier,
    string Landmark,
    string? Instructions,
    double Latitude,
    double Longitude);
