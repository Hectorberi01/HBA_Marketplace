using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Tableau de bord du livreur (§15).</summary>
/// <param name="Status">
/// <summary> « Available », « Unavailable », « OnMission »… — tel que le service le
/// dit.</summary>
/// </param>
/// <param name="CurrentMission"><summary>La mission en cours, s'il y en a une.</summary></param>
public sealed record DriverDashboardDto(
    DriverProfileDto Driver,
    string Status,
    DriverMissionDto? CurrentMission,

    DriverTodayDto Today);

/// <summary>Les chiffres du jour.</summary>
public sealed record DriverTodayDto(
    int LifetimeDeliveries,
    decimal? AvailableBalance,
    decimal? LifetimeEarned,
    string? Currency);

public sealed record DriverProfileDto(
    Guid DriverId,
    string FullName,
    string Phone,
    string Vehicle,
    string AccountStatus,
    string? StatusReason,
    int CompletedDeliveries,
    DateTime RegisteredAtUtc,
    DateTime? VerifiedAtUtc);

/// <summary>Une mission, telle que le livreur a le droit de la voir (§16).</summary>
/// <param name="RequiredProof">
/// <summary> « Otp », « Photo », « Signature » — imposé par le service.</summary>
/// </param>
public sealed record DriverMissionDto(
    Guid DeliveryId,
    string Reference,
    string Status,
    string Type,
    DriverStopDto Pickup,
    DriverStopDto Dropoff,
    string? PackageDescription,
    decimal? PackageWeightKg,
    bool IsFragile,
    string RequiredProof,

    decimal? EstimatedEarning,
    string? Currency,
    DateTime? ScheduledForUtc,
    DateTime? OfferExpiresAtUtc);

/// <summary>Un point de la course.</summary>
public sealed record DriverStopDto(
    string ContactName,
    string Phone,
    string CommuneName,
    string? Quartier,
    string Landmark,
    string? Instructions,
    double Latitude,
    double Longitude);

/// <summary>Écran « Revenus » (§15).</summary>
public sealed record DriverEarningsDto(
    decimal? AvailableBalance,
    decimal? LifetimeEarned,
    string? Currency,
    PagedResult<DriverMovementDto> Movements);

/// <param name="Direction">
/// <summary> « Credit » ou « Debit », tel que le service le nomme.</summary>
/// </param>
public sealed record DriverMovementDto(
    Guid Id,
    string Direction,

    decimal Amount,
    string Currency,
    string Reason,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTime CreatedAtUtc);
