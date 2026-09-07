// CE FICHIER EST LE « DOSSIER » DU LIVREUR, PAS SA PROJECTION DISPATCHABLE.

using HBA.Delivery.Driver.Domain.Entities;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Events;
using HBA.Delivery.Driver.Domain.Policies;
using HBA.Delivery.Driver.Domain.ValueObjects;
using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Delivery.Driver.Domain.Aggregates;

/// <summary>LE DOSSIER D'UN LIVREUR.</summary>
public sealed class DriverAccount : AggregateRoot<Guid>
{
    private readonly List<DriverDocument> _documents = new();
    private readonly List<DriverVehicle> _vehicles = new();

    private DriverAccount(Guid id, Guid userId, string fullName, string phone)
        : base(id)
    {
        UserId = userId;
        FullName = fullName;
        Phone = phone;
        VerificationStatus = DriverVerificationStatus.PendingDocuments;
        RegisteredAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private DriverAccount()
    {
        FullName = string.Empty;
        Phone = string.Empty;
    }

    /// <summary>Compte HBA. L'identité vit dans identity-service, pas ici.</summary>
    public Guid UserId { get; private set; }

    public string FullName { get; private set; }

    public string Phone { get; private set; }

    public DriverVerificationStatus VerificationStatus { get; private set; }

    /// <summary>Motif du refus ou de la suspension.</summary>
    public string? StatusReason { get; private set; }

    public DateTime RegisteredAtUtc { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    public IReadOnlyCollection<DriverDocument> Documents => _documents.AsReadOnly();

    public IReadOnlyCollection<DriverVehicle> Vehicles => _vehicles.AsReadOnly();

    /// <summary>Le dossier autorise-t-il à travailler ?</summary>
    public bool IsDispatchable => VerificationStatus is DriverVerificationStatus.Verified;

    /// <summary>Véhicule actif déclaré, s'il y en a un.</summary>
    public DriverVehicle? ActiveVehicle => _vehicles.FirstOrDefault(vehicle => vehicle.Active);

    // ─── Inscription ────────────────────────────────────────────────────────

    /// <summary>Ouvre un dossier pour l'utilisateur du jeton.</summary>
    public static Result<DriverAccount> Register(Guid userId, string? fullName, string? phone)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<DriverAccount>(
                Error.Validation("driver.user_required", "Un compte utilisateur est requis."));
        }

        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        if (name is null)
        {
            return Result.Failure<DriverAccount>(
                Error.Validation("driver.name_required", "Le nom du livreur est requis."));
        }

        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure<DriverAccount>(
                Error.Validation(
                    "driver.phone_invalid",
                    $"Un numéro joignable est requis ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        var account = new DriverAccount(Guid.NewGuid(), userId, name, normalizedPhone);
        account.Raise(new DriverAccountRegisteredDomainEvent(account.Id, userId));
        return account;
    }

    public Result UpdateProfile(string? fullName, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            FullName = fullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var normalized = BeninGeography.NormalizePhone(phone);
            if (normalized is null)
            {
                return Result.Failure(
                    Error.Validation("driver.phone_invalid", "Numéro de téléphone invalide."));
            }

            Phone = normalized;
        }

        return Result.Success();
    }

    // ─── Véhicules ──────────────────────────────────────────────────────────

    /// <summary>
    /// Déclare un véhicule. Le dernier déclaré devient l'actif, et les autres
    /// cessent de l'être : un livreur ne conduit qu'un véhicule à la fois, et
    /// laisser deux véhicules actifs ferait dépendre la capacité de charge retenue
    /// de l'ordre d'énumération.
    /// </summary>
    public Result<DriverVehicle> DeclareVehicle(
        DriverVehicleType type, string? make, string? model, string? plate, decimal? capacityKg)
    {
        // LA PLAQUE N'EST EXIGÉE QUE DES VÉHICULES QUI EN PORTENT UNE.
        string? plaque = null;
        if (DriverVehicleTypes.RequiresPlate(type))
        {
            var lue = VehiclePlate.Create(plate);
            if (lue.IsFailure)
            {
                return Result.Failure<DriverVehicle>(lue.Error);
            }

            plaque = lue.Value.Value;
        }
        else if (!string.IsNullOrWhiteSpace(plate))
        {
            plaque = VehiclePlate.Normalize(plate);
        }

        if (capacityKg is < 0)
        {
            return Result.Failure<DriverVehicle>(
                Error.Validation("driver.capacity_invalid", "La capacité de charge ne peut pas être négative."));
        }

        foreach (var existing in _vehicles)
        {
            existing.Deactivate();
        }

        var vehicle = DriverVehicle.Declare(Id, type, make, model, plaque, capacityKg);
        _vehicles.Add(vehicle);
        Raise(new DriverVehicleDeclaredDomainEvent(Id, vehicle.Id, type));
        return vehicle;
    }

    // ─── Pièces justificatives ──────────────────────────────────────────────

    /// <summary>
    /// Dépose une pièce. Redéposer le MÊME type remplace la précédente et la remet
    /// en attente : c'est le geste d'un livreur dont la pièce a été refusée, ou
    /// dont le permis a été renouvelé.
    /// </summary>
    public Result<DriverDocument> SubmitDocument(DriverDocumentType type, string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Result.Failure<DriverDocument>(
                Error.Validation("driver.document_missing", "La pièce déposée est vide."));
        }

        if (VerificationStatus is DriverVerificationStatus.Suspended)
        {
            return Result.Failure<DriverDocument>(
                Error.Conflict("driver.suspended", "Ce dossier est suspendu : contactez l'exploitation."));
        }

        _documents.RemoveAll(document => document.Type == type);

        var submitted = DriverDocument.Submit(Id, type, objectKey.Trim());
        _documents.Add(submitted);

        // Redéposer une pièce APRÈS une vérification rouvre le dossier.
        if (VerificationStatus is DriverVerificationStatus.Verified or DriverVerificationStatus.Rejected)
        {
            VerificationStatus = DriverVerificationStatus.PendingDocuments;
            StatusReason = null;
            DecidedAtUtc = null;
        }

        return submitted;
    }

    /// <summary>Soumet le dossier à l'exploitation.</summary>
    public Result SubmitForReview()
    {
        if (VerificationStatus is DriverVerificationStatus.UnderReview)
        {
            return Result.Success();
        }

        if (VerificationStatus is DriverVerificationStatus.Suspended)
        {
            return Result.Failure(
                Error.Conflict("driver.suspended", "Ce dossier est suspendu : contactez l'exploitation."));
        }

        var manquantes = DriverDocumentPolicy.MissingRequired(_documents.Select(document => document.Type));
        if (manquantes.Count > 0)
        {
            return Result.Failure(Error.BusinessRule(
                "driver.documents_incomplete",
                "Pièces manquantes : " + string.Join(", ", manquantes)));
        }

        if (ActiveVehicle is null)
        {
            return Result.Failure(Error.BusinessRule(
                "driver.vehicle_required",
                "Déclarez le véhicule avec lequel vous livrerez avant de soumettre votre dossier."));
        }

        VerificationStatus = DriverVerificationStatus.UnderReview;
        SubmittedAtUtc = DateTime.UtcNow;
        StatusReason = null;
        return Result.Success();
    }

    // ─── Décisions de l'exploitation ────────────────────────────────────────

    /// <summary>Vérifie le dossier.</summary>
    public Result Verify()
    {
        if (VerificationStatus is DriverVerificationStatus.Verified)
        {
            return Result.Success();
        }

        if (VerificationStatus is DriverVerificationStatus.PendingDocuments)
        {
            return Result.Failure(Error.Conflict(
                "driver.not_submitted",
                "Ce dossier n'a pas encore été soumis : les pièces obligatoires manquent."));
        }

        var vehicle = ActiveVehicle;
        if (vehicle is null)
        {
            return Result.Failure(Error.Conflict(
                "driver.vehicle_required", "Ce dossier ne déclare aucun véhicule actif."));
        }

        foreach (var document in _documents)
        {
            document.Approve();
        }

        VerificationStatus = DriverVerificationStatus.Verified;
        StatusReason = null;
        DecidedAtUtc = DateTime.UtcNow;

        // C'EST CET ÉVÉNEMENT QUI CRÉE LA PROJECTION DISPATCHABLE CHEZ
        // delivery-service.
        Raise(new DriverAccountVerifiedDomainEvent(Id, UserId, FullName, Phone, vehicle.Type));

        return Result.Success();
    }

    public Result Reject(string? reason)
    {
        if (VerificationStatus is DriverVerificationStatus.Verified)
        {
            return Result.Failure(Error.Conflict(
                "driver.already_verified",
                "Ce dossier est déjà vérifié : utilisez la suspension pour l'écarter."));
        }

        VerificationStatus = DriverVerificationStatus.Rejected;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecidedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Suspend le dossier.</summary>
    public Result Suspend(string? reason)
    {
        VerificationStatus = DriverVerificationStatus.Suspended;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DecidedAtUtc = DateTime.UtcNow;
        Raise(new DriverAccountSuspendedDomainEvent(Id, UserId, StatusReason));
        return Result.Success();
    }
}
