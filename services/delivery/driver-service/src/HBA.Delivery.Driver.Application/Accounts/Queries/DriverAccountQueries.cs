using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Policies;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Drivers.Application.Accounts.Queries;

/// <summary>
/// Mon dossier. `UserId` vient du jeton — voir l'encadré des commandes voisines.
/// </summary>
public sealed record GetMyDriverAccountQuery(Guid UserId) : IQuery<DriverAccountDto>;

/// <summary>Un dossier vu par l'exploitation ou par le port interne.</summary>
public sealed record GetDriverAccountQuery(Guid DriverId) : IQuery<DriverAccountDto>;

/// <summary>
/// La file de vérification.
///
/// SANS CETTE LECTURE, LA VÉRIFICATION EST INAPPLICABLE : un livreur s'inscrit,
/// personne n'est alerté, et le seul moyen de le retrouver serait de connaître son
/// identifiant — que lui seul possède. Une route « vérifier ce dossier » sans
/// route « qui attend ? » est un bouton sans liste.
/// </summary>
public sealed record ListDriverAccountsQuery(DriverVerificationStatus Status, int Take = 100)
    : IQuery<IReadOnlyList<DriverAccountDto>>;

/// <summary>Ce livreur a-t-il le droit de livrer, et avec ce véhicule ?</summary>
public sealed record CheckDriverEligibilityQuery(Guid DriverId, string? RequiredVehicleType)
    : IQuery<DriverEligibilityDto>;

internal sealed class DriverAccountQueryHandler
    : IQueryHandler<GetMyDriverAccountQuery, DriverAccountDto>,
      IQueryHandler<GetDriverAccountQuery, DriverAccountDto>,
      IQueryHandler<ListDriverAccountsQuery, IReadOnlyList<DriverAccountDto>>,
      IQueryHandler<CheckDriverEligibilityQuery, DriverEligibilityDto>
{
    private readonly IDriverAccountRepository _accounts;

    public DriverAccountQueryHandler(IDriverAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<Result<DriverAccountDto>> Handle(
        GetMyDriverAccountQuery query, CancellationToken cancellationToken)
    {
        if (query.UserId == Guid.Empty)
        {
            return Result.Failure<DriverAccountDto>(
                Error.Unauthorized("driver.unauthenticated", "Aucun compte dans le jeton présenté."));
        }

        var account = await _accounts.GetByUserIdAsync(query.UserId, cancellationToken);
        return account is null ? Result.Failure<DriverAccountDto>(NotFound()) : ToDto(account);
    }

    public async Task<Result<DriverAccountDto>> Handle(
        GetDriverAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdAsync(query.DriverId, cancellationToken);
        return account is null ? Result.Failure<DriverAccountDto>(NotFound()) : ToDto(account);
    }

    public async Task<Result<IReadOnlyList<DriverAccountDto>>> Handle(
        ListDriverAccountsQuery query, CancellationToken cancellationToken)
    {
        // La borne est resserrée ici et non dans la route : une file de
        // vérification appelée avec `take=100000` chargerait tous les dossiers,
        // leurs pièces et leurs véhicules dans une seule réponse.
        var take = Math.Clamp(query.Take, 1, 200);

        var accounts = await _accounts.ListByStatusAsync(query.Status, take, cancellationToken);

        IReadOnlyList<DriverAccountDto> dtos = accounts.Select(ToDto).ToList();
        return Result.Success(dtos);
    }

    public async Task<Result<DriverEligibilityDto>> Handle(
        CheckDriverEligibilityQuery query, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetByIdAsync(query.DriverId, cancellationToken);

        // « INTROUVABLE » EST UNE RÉPONSE, PAS UNE ERREUR. L'appelant est un
        // service interne qui pose une question fermée ; lui rendre un 404 le
        // forcerait à traiter deux formes de « non » au lieu d'une.
        if (account is null)
        {
            return new DriverEligibilityDto(query.DriverId, false, "DRIVER_NOT_FOUND");
        }

        if (!account.IsDispatchable)
        {
            return new DriverEligibilityDto(query.DriverId, false, "DRIVER_NOT_VERIFIED");
        }

        var vehicle = account.ActiveVehicle;
        if (vehicle is null)
        {
            return new DriverEligibilityDto(query.DriverId, false, "DRIVER_NO_VEHICLE");
        }

        // Le type demandé arrive EN TEXTE : c'est le contrat interne, et un
        // appelant n'a pas à connaître nos valeurs numériques. Un type inconnu
        // n'est pas une erreur de l'appelant — c'est simplement « non ».
        if (!string.IsNullOrWhiteSpace(query.RequiredVehicleType)
            && !string.Equals(vehicle.Type.ToString(), query.RequiredVehicleType, StringComparison.OrdinalIgnoreCase))
        {
            return new DriverEligibilityDto(query.DriverId, false, "DRIVER_VEHICLE_MISMATCH");
        }

        return new DriverEligibilityDto(query.DriverId, true, null);
    }

    private static Error NotFound()
        => Error.NotFound("driver.not_found", "Aucun dossier livreur n'est rattaché à ce compte.");

    private static DriverAccountDto ToDto(DriverAccount account) => new(
        account.Id,
        account.UserId,
        account.FullName,
        account.Phone,
        account.VerificationStatus.ToString(),
        account.StatusReason,
        account.IsDispatchable,
        account.RegisteredAtUtc,
        account.SubmittedAtUtc,
        account.DecidedAtUtc,
        account.Documents
            .Select(document => new DriverDocumentDto(
                document.Id,
                document.Type.ToString(),
                document.Status.ToString(),
                document.SubmittedAtUtc,
                document.ReviewedAtUtc,
                document.RejectionReason))
            .ToList(),
        account.Vehicles
            .Select(vehicle => new DriverVehicleDto(
                vehicle.Id,
                vehicle.Type.ToString(),
                vehicle.Make,
                vehicle.Model,
                vehicle.Plate,
                vehicle.Active,
                vehicle.CapacityKg))
            .ToList(),

        // Rendu au livreur pour qu'il sache QUOI déposer. Sans cette liste,
        // l'écran ne peut afficher que « dossier incomplet », et le livreur
        // redépose au hasard.
        DriverDocumentPolicy.MissingRequired(account.Documents.Select(document => document.Type)));
}
