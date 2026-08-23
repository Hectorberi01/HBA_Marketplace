using HBA.Gateway.Application.Contracts.Delivery;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>
/// Projections partagées par les agrégations livreur.
/// </summary>
/// <remarks>
/// UNE SEULE TRADUCTION MISSION → DTO, POUR UNE RAISON DE SÉCURITÉ.
///
/// Le tableau de bord, la liste des missions et le détail rendent tous une
/// mission. Trois traductions séparées, c'est trois occasions d'oublier de
/// retirer <c>Price</c> — et une seule suffit pour rendre la marge de la
/// plateforme calculable par soustraction.
///
/// Ce fichier est donc le seul endroit où l'on décide ce qu'un livreur voit.
/// </remarks>
public static class DriverProjections
{
    /// <summary>
    /// Statuts d'une mission qui occupe le livreur MAINTENANT.
    /// </summary>
    /// <remarks>
    /// LISTE RELEVÉE DANS LE DOMAINE, À CONFIRMER.
    ///
    /// delivery-service n'expose aucun filtre de statut : la sélection se fait
    /// ici. Un statut manquant fait disparaître la mission en cours de l'accueil
    /// — sans erreur, ce qui ressemble à « je n'ai pas de mission ».
    /// </remarks>
    private static readonly string[] ActiveStatuses =
    [
        "Assigned", "Accepted", "GoingToPickup", "ArrivedAtPickup",
        "PickedUp", "InTransit", "ArrivedAtDropoff",
    ];

    public static bool IsActive(string status)
        => ActiveStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// `Price` N'EST PAS RECOPIÉ. NE PAS L'AJOUTER.
    ///
    /// Cf. <see cref="DriverMissionDto"/> : l'écart entre le prix client et le
    /// gain livreur EST la marge de la plateforme.
    /// </summary>
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
