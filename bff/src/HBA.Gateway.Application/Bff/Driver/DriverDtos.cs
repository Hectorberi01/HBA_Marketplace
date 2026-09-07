using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Driver;

/// <summary>Tableau de bord du livreur (§15).</summary>
/// <param name="Status">
/// <summary>« Available », « Unavailable », « OnMission »… — tel que le service le dit.</summary>
/// </param>
/// <param name="CurrentMission">
/// <summary>La mission en cours, s'il y en a une.</summary>
/// </param>
public sealed record DriverDashboardDto(
    DriverProfileDto Driver,
    string Status,
    DriverMissionDto? CurrentMission,

    DriverTodayDto Today);

/// <summary>
/// Les chiffres du jour.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `Deliveries` EST LE CUMUL DEPUIS L'INSCRIPTION, PAS CELUI DU JOUR.
///
/// L'application affiche « 8 livraisons aujourd'hui ». Ni delivery-service ni
/// financial-service n'exposent de compteur JOURNALIER : le premier ne connaît
/// que <c>CompletedDeliveries</c> (total de la vie du compte), le second ne rend
/// que le solde et le cumul.
///
/// Le calculer ici supposerait de récupérer toutes les missions et de filtrer sur
/// une date — sur une liste sans pagination ni filtre, c'est-à-dire en tirant
/// l'historique complet d'un livreur à chaque affichage de son accueil.
///
/// Le champ porte donc ce qu'il EST — un cumul — et le nom le dit. Afficher un
/// cumul de vie sous l'étiquette « aujourd'hui » ferait croire à un livreur qu'il
/// a fait 1 284 courses depuis ce matin.
///
/// Manque à combler : <c>GET /api/deliveries/drivers/me/stats?from=&amp;to=</c>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// Une mission, telle que le livreur a le droit de la voir (§16).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// NI `Price`, NI MARGE, NI COMMISSION — LE §16 L'INTERDIT.
///
/// Le contrat amont porte DEUX montants : <c>Price</c>, ce que paie le client, et
/// <c>EstimatedEarning</c>, ce que touche le livreur. L'écart entre les deux EST
/// la marge de la plateforme. Transporter les deux jusqu'à un téléphone la rend
/// calculable par soustraction — par n'importe qui inspectant la réponse.
///
/// Seul le gain part. C'est aussi le seul des deux qui intéresse celui qui roule.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
/// <param name="RequiredProof">
/// <summary>« Otp », « Photo », « Signature » — imposé par le service.</summary>
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

/// <summary>
/// Un point de la course.
/// </summary>
/// <remarks>
/// LE TÉLÉPHONE EST TRANSMIS TEL QUE LE SERVICE LE REND.
///
/// Le §16 demande un « contact sécurisé ». delivery-service rend le numéro en
/// clair : c'est à LUI de masquer ou de fournir un relais, pas à la passerelle de
/// filtrer après coup. Un masquage appliqué ici serait contournable en
/// interrogeant le service directement, et donnerait une fausse impression de
/// protection. Relevé dans les manques.
/// </remarks>
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
/// <summary>« Credit » ou « Debit », tel que le service le nomme.</summary>
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
