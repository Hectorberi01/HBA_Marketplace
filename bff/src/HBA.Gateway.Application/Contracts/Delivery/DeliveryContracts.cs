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

/// <summary>
/// Une mission telle que le livreur la voit — miroir de <c>MyDeliveryDto</c>.
/// </summary>
/// <remarks>
/// `EstimatedEarning` EST LA PART DU LIVREUR, `Price` CELLE DU CLIENT.
///
/// Les deux existent dans le contrat amont et ne veulent pas dire la même chose.
/// Le §16 interdit d'exposer la marge HBA : ne transporter que l'un des deux
/// serait plus sûr, mais `Price` sert à justifier un litige de facturation. Le
/// DTO de sortie, lui, ne garde que le gain — cf. `DriverMissionDto`.
/// </remarks>
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

/// <summary>
/// Un point de la course.
/// </summary>
/// <remarks>
/// LE TÉLÉPHONE Y FIGURE, ET C'EST LE SERVICE QUI EN DÉCIDE.
///
/// Le contrat amont le porte en clair. Le §16 demande un « contact sécurisé » :
/// c'est à delivery-service de décider s'il masque, pas à la passerelle de
/// filtrer ce qu'il a choisi d'envoyer. Signalé au relevé des manques.
/// </remarks>
public sealed record DriverStop(
    string ContactName,
    string Phone,
    string CommuneName,
    string? Quartier,
    string Landmark,
    string? Instructions,
    double Latitude,
    double Longitude);
