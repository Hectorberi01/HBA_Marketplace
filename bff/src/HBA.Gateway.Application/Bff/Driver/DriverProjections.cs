using HBA.Gateway.Application.Contracts.Delivery;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Projections partagées par les agrégations livreur.</summary>
public static class DriverProjections
{
    /// <summary>Statuts d'une mission qui occupe le livreur MAINTENANT.</summary>
    private static readonly string[] ActiveStatuses =
    [
        "Assigned", "Accepted", "GoingToPickup", "ArrivedAtPickup",
        "PickedUp", "InTransit", "ArrivedAtDropoff",
    ];

    public static bool IsActive(string status)
        => ActiveStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>`Price` N'EST PAS RECOPIÉ.</summary>
    public static DriverMissionDto ToDto(DriverMission mission)
        => new(
            mission.DeliveryId,
            mission.Reference,
            mission.Status,
            mission.Type,
            ToDto(mission.Pickup),
            ToDto(mission.Dropoff),
            mission.PackageDescription,
            mission.PackageWeightKg,
            mission.IsFragile,
            mission.RequiredProof,
            mission.EstimatedEarning,
            mission.Currency,
            mission.ScheduledForUtc,
            mission.OfferExpiresAtUtc);

    public static DriverStopDto ToDto(DriverStop stop)
        => new(
            stop.ContactName,
            stop.Phone,
            stop.CommuneName,
            stop.Quartier,
            stop.Landmark,
            stop.Instructions,
            stop.Latitude,
            stop.Longitude);

    public static DriverProfileDto ToDto(DriverAccount account)
        => new(
            account.DriverId,
            account.FullName,
            account.Phone,
            account.Vehicle,
            account.AccountStatus,
            account.StatusReason,
            account.CompletedDeliveries,
            account.RegisteredAtUtc,
            account.VerifiedAtUtc);
}
