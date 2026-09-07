namespace HBA.Deliveries.Contracts;

/// <summary>CE QUE LE MODULE DELIVERIES ACCEPTE DE MONTRER.</summary>
/// <param name="Id">Identifiant de la course.</param>
/// <param name="Reference">Référence du donneur d'ordre, telle qu'il l'a fournie.</param>
/// <param name="Source">« HbaExpress », « HbaFood » ou « ExternalPartner ».</param>
/// <param name="Status">État courant, en clair.</param>
/// <param name="DriverName">Livreur en charge, s'il y en a un.</param>
/// <param name="DriverPhone">Son numéro : c'est ce que le client réclame en premier.</param>
public sealed record DeliverySummary(
    Guid Id,
    string Reference,
    string Source,
    string Type,
    string Status,
    string PickupSummary,
    string DropoffSummary,
    string? DriverName,
    string? DriverPhone,
    DateTime CreatedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? PickedUpAtUtc,
    DateTime? DeliveredAtUtc);

/// <summary>Suivi temps réel d'une course.</summary>
public sealed record DeliveryTracking(
    Guid DeliveryId,
    string Status,
    double? DriverLatitude,
    double? DriverLongitude,
    DateTime? PositionReportedAtUtc,
    string? DriverName,
    string? DriverPhone);

/// <summary>API en processus du module Deliveries.</summary>
/// <summary>
/// Le strict nécessaire pour s'adresser à un livreur depuis un autre module : à
/// quel compte il correspond, et comment le nommer.
/// </summary>
public sealed record DriverAccount(Guid DriverId, Guid UserId, string FullName);

public interface IDeliveryModuleApi
{
    Task<DeliverySummary?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    /// <summary>À quel compte utilisateur ce livreur correspond-il ?</summary>
    Task<DriverAccount?> GetDriverAccountAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve la course rattachée à une référence de commande.</summary>
    Task<DeliverySummary?> GetByReferenceAsync(string reference, string source, CancellationToken cancellationToken = default);

    Task<DeliveryTracking?> GetTrackingAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le compte d'un livreur, vu PAR LUI-MÊME.
/// </summary>
/// <remarks>
/// CE CONTRAT MANQUAIT, ET IL BLOQUAIT TOUT L'ÉCRAN « GAINS ».
///
/// Le jeton d'un livreur porte un <c>userId</c>. Le portefeuille, lui, vit dans
/// financial-service sous <c>GET /api/financial/wallets/drivers/{driverId}</c> —
/// un identifiant DIFFÉRENT, que rien n'exposait au-dehors.
///
/// <c>ResolveDriverQuery</c> faisait déjà la conversion, mais en interne
/// uniquement : chaque route de l'espace livreur l'appelait pour elle-même.
/// Aucune couche extérieure — passerelle comprise — ne pouvait obtenir ce
/// <c>driverId</c>, et l'agrégation « mes gains » était donc impossible à écrire.
///
/// NE PAS Y AJOUTER LA POSITION.
///
/// <c>LastKnownPosition</c> existe sur l'agrégat, et il serait tentant de
/// l'exposer « puisqu'on y est ». C'est une donnée de dispatch, pas de profil :
/// la rendre lisible par le compte lui-même n'apporte rien, et la rendre lisible
/// tout court élargit la surface de ce que la plateforme divulgue d'un
/// travailleur.
/// </remarks>
/// <param name="AccountStatus">
/// <summary>« PendingVerification », « Active », « Suspended », « Blocked ».</summary>
/// </param>
/// <param name="Availability">
/// <summary>« Available », « Unavailable », « OnMission »… — l'état du jour.</summary>
/// </param>
/// <param name="StatusReason">
/// <summary>Motif d'une suspension ou d'un blocage. Nul si le compte est sain.</summary>
/// </param>
public sealed record DriverAccountView(
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
