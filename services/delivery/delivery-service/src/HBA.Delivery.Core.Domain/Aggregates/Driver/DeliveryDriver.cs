// CE FICHIER VIVAIT DANS `driver-service/src/HBA.Delivery.Driver.Domain`.

using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers.Events;
using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>État du COMPTE. Décidé par l'exploitation, change rarement.</summary>
public enum DriverAccountStatus
{
    /// <summary>Inscrit, pièces non encore validées.</summary>
    PendingVerification = 0,

    /// <summary>Autorisé à travailler.</summary>
    Active = 1,

    /// <summary>Suspendu temporairement.</summary>
    Suspended = 2,

    /// <summary>Bloqué définitivement.</summary>
    Blocked = 3
}

/// <summary>
/// Disponibilité OPÉRATIONNELLE. Décidée par le livreur, change plusieurs fois par
/// jour.
/// </summary>
public enum DriverAvailability
{
    /// <summary>Hors ligne : ne reçoit aucune proposition.</summary>
    Offline = 0,

    /// <summary>En ligne et libre.</summary>
    Available = 1,

    /// <summary>En ligne mais déjà sur une course.</summary>
    Busy = 2,

    /// <summary>En ligne, en pause.</summary>
    OnBreak = 3
}

public enum VehicleType
{
    /// <summary>Deux-roues. L'écrasante majorité de la flotte à Cotonou.</summary>
    Motorcycle = 0,

    Bicycle = 1,
    Car = 2,
    Van = 3,
    OnFoot = 4,

    /// <summary>
    /// Tricycle à moteur. Ajouté après coup : il manquait, alors qu'il occupe au
    /// Bénin la place exacte entre la moto et la camionnette — jusqu'à environ 150
    /// kg, dans des ruelles où une voiture ne passe pas.
    /// </summary>
    Tricycle = 5
}

/// <summary>UN LIVREUR.</summary>
public sealed class Driver : AggregateRoot<DriverId>
{
    private Driver(DriverId id, Guid userId, string fullName, string phone, VehicleType vehicle)
        : base(id)
    {
        UserId = userId;
        FullName = fullName;
        Phone = phone;
        Vehicle = vehicle;
        AccountStatus = DriverAccountStatus.PendingVerification;
        Availability = DriverAvailability.Offline;
        RegisteredAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private Driver()
    {
        FullName = string.Empty;
        Phone = string.Empty;
    }

    /// <summary>Compte HBA correspondant.</summary>
    public Guid UserId { get; private set; }

    public string FullName { get; private set; }

    public string Phone { get; private set; }

    public VehicleType Vehicle { get; private set; }

    public DriverAccountStatus AccountStatus { get; private set; }

    public DriverAvailability Availability { get; private set; }

    public DateTime RegisteredAtUtc { get; private set; }

    public DateTime? VerifiedAtUtc { get; private set; }

    /// <summary>Motif de la dernière suspension ou du blocage.</summary>
    public string? StatusReason { get; private set; }

    /// <summary>Dernière position connue, recopiée depuis Redis de loin en loin.</summary>
    public Coordinates? LastKnownPosition { get; private set; }

    public DateTime? LastPositionAtUtc { get; private set; }

    /// <summary>Nombre de courses menées à leur terme.</summary>
    public int CompletedDeliveries { get; private set; }

    /// <summary>
    /// Peut-il recevoir une proposition ? C'est la SEULE question que le dispatch
    /// doit poser — jamais l'un des deux champs pris isolément.
    /// </summary>
    public bool CanReceiveOffers =>
        AccountStatus is DriverAccountStatus.Active && Availability is DriverAvailability.Available;

    /// <summary>Inscrit un livreur dans la projection dispatchable.</summary>
    /// <param name="id">AJOUTÉ AU LOT 5.2 POUR QUE LE LIVREUR N'AIT PAS DEUX IDENTIFIANTS.</param>
    public static Result<Driver> Register(
        Guid userId, string? fullName, string? phone, VehicleType vehicle, DriverId? id = null)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.user_required", "Un compte utilisateur est requis."));
        }

        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        if (name is null)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.name_required", "Le nom du livreur est requis."));
        }

        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure<Driver>(
                Error.Validation("driver.phone_invalid",
                    $"Un numéro joignable est requis ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        return new Driver(id ?? DriverId.New(), userId, name, normalizedPhone, vehicle);
    }

    // ─── Décisions de l'exploitation ────────────────────────────────────────

    public Result Verify()
    {
        if (AccountStatus is DriverAccountStatus.Blocked)
        {
            return Result.Failure(
                Error.Conflict("driver.blocked", "Ce compte est bloqué : il ne peut pas être vérifié."));
        }

        // IDEMPOTENTE. Vérifier deux fois — double clic, rejeu — ne doit pas
        // repousser la date de vérification : elle répond à « depuis quand ce
        // livreur est-il autorisé à travailler ? », et l'écraser effacerait
        // l'ancienneté qui sert justement à arbitrer les litiges.
        if (AccountStatus is DriverAccountStatus.Active)
        {
            return Result.Success();
        }

        AccountStatus = DriverAccountStatus.Active;
        VerifiedAtUtc ??= DateTime.UtcNow;
        StatusReason = null;

        // Le livreur devient une personne autorisée à travailler pour HBA. C'est à
        // ce moment précis que le rôle « Driver » doit lui être attribué côté
        // Identity — pas à l'inscription, où n'importe qui peut se déclarer
        // livreur.
        Raise(new DriverVerifiedDomainEvent(Id.Value, UserId));

        return Result.Success();
    }

    /// <summary>
    /// Suspend le compte. Le livreur passe HORS LIGNE dans le même geste : le
    /// laisser « disponible » avec un compte suspendu créerait exactement
    /// l'incohérence que la séparation des deux dimensions cherche à éviter.
    /// </summary>
    public Result Suspend(string? reason)
    {
        if (AccountStatus is DriverAccountStatus.Blocked)
        {
            return Result.Failure(
                Error.Conflict("driver.blocked", "Ce compte est déjà bloqué."));
        }

        AccountStatus = DriverAccountStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    public Result Block(string? reason)
    {
        AccountStatus = DriverAccountStatus.Blocked;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    // ─── Décisions du livreur ───────────────────────────────────────────────

    public Result GoOnline()
    {
        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return Result.Failure(Error.Forbidden(
                "driver.not_active",
                "Votre compte n'est pas actif : la vérification de vos pièces doit d'abord être terminée."));
        }

        Availability = DriverAvailability.Available;
        return Result.Success();
    }

    public Result GoOffline()
    {
        // Passer hors ligne pendant une course est REFUSÉ : le colis est chez le
        // livreur, et le client attend.
        if (Availability is DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict(
                "driver.busy",
                "Terminez votre course en cours avant de passer hors ligne."));
        }

        Availability = DriverAvailability.Offline;
        return Result.Success();
    }

    public Result TakeBreak()
    {
        if (Availability is DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict("driver.busy", "Terminez votre course en cours avant de faire une pause."));
        }

        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return Result.Failure(Error.Forbidden("driver.not_active", "Votre compte n'est pas actif."));
        }

        Availability = DriverAvailability.OnBreak;
        return Result.Success();
    }

    // ─── Cycle de mission ───────────────────────────────────────────────────

    /// <summary>Passe le livreur en mission.</summary>
    public Result MarkBusy()
    {
        if (!CanReceiveOffers)
        {
            return Result.Failure(Error.Conflict("driver.unavailable", "Ce livreur n'est pas disponible."));
        }

        Availability = DriverAvailability.Busy;
        return Result.Success();
    }

    /// <summary>Fin de mission : le livreur redevient disponible et son compteur avance.</summary>
    public Result CompleteMission()
    {
        if (Availability is not DriverAvailability.Busy)
        {
            return Result.Failure(Error.Conflict("driver.not_on_mission", "Ce livreur n'est pas en mission."));
        }

        CompletedDeliveries++;
        Availability = DriverAvailability.Available;
        return Result.Success();
    }

    /// <summary>Recopie la position depuis le cache.</summary>
    public void RecordPosition(Coordinates position)
    {
        if (AccountStatus is not DriverAccountStatus.Active)
        {
            return;
        }

        LastKnownPosition = position;
        LastPositionAtUtc = DateTime.UtcNow;
    }
}
