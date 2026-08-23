using HBA.Delivery.Driver.Domain.Aggregates;
using HBA.Delivery.Driver.Domain.Enums;
using HBA.Delivery.Driver.Domain.Repositories;
using HBA.Drivers.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Drivers.Application.Accounts.Commands;

// ═════════════════════════════════════════════════════════════════════════════
// TOUTES CES COMMANDES PORTENT `UserId` EN PREMIER, ET C'EST UNE GARDE DE FORME.
//
// AVANT CE LOT, AUCUNE N'EXISTAIT : les six routes `/api/v1/drivers/me*`
// opéraient sur `DriverStore.DefaultDriverId`, un GUID codé en dur. TOUS LES
// LIVREURS ÉTAIENT LE MÊME LIVREUR (ISSUE-029) : un livreur lisait le dossier
// d'un autre, modifiait son téléphone et déclarait son véhicule.
//
// La correction n'est pas « vérifier l'identifiant reçu » mais NE JAMAIS EN
// RECEVOIR : le `UserId` est écrit par la route depuis `CurrentUserId(user)`,
// c'est-à-dire depuis le jeton. C'est le raisonnement développé dans
// `FinancialEndpoints.cs` autour des routes `/me` (ISSUE-017/018), et il vaut
// ici mot pour mot — le propriétaire du dossier EST l'utilisateur du jeton, il
// n'y a donc aucun lien à vérifier et surtout aucune surface où il faudrait le
// vérifier.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Ouvre un dossier livreur pour l'utilisateur du jeton.</summary>
public sealed record RegisterDriverCommand(Guid UserId, string? FullName, string? Phone) : ICommand<Guid>;

/// <summary>Met à jour nom et téléphone du dossier de l'appelant.</summary>
public sealed record UpdateDriverProfileCommand(Guid UserId, string? FullName, string? Phone) : ICommand;

/// <summary>Déclare le véhicule avec lequel l'appelant livrera.</summary>
public sealed record DeclareVehicleCommand(
    Guid UserId,
    DriverVehicleType Type,
    string? Make,
    string? Model,
    string? Plate,
    decimal? CapacityKg) : ICommand<Guid>;

/// <summary>Dépose une pièce justificative dans le dossier de l'appelant.</summary>
public sealed record SubmitDriverDocumentCommand(
    Guid UserId, DriverDocumentType Type, string? ObjectKey) : ICommand<Guid>;

/// <summary>Soumet le dossier de l'appelant à la vérification.</summary>
public sealed record SubmitDriverDossierCommand(Guid UserId) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CINQ GESTES DU LIVREUR SUR SON PROPRE DOSSIER.
///
/// Toutes suivent le même moule : résoudre le dossier PAR LE COMPTE, appeler la
/// méthode du domaine, enregistrer. Aucune ne décide quoi que ce soit — la
/// complétude des pièces, la validité du téléphone et l'exigence de plaque sont
/// dans l'agrégat, qui est le seul à pouvoir les tenir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DriverAccountCommandHandler
    : ICommandHandler<RegisterDriverCommand, Guid>,
      ICommandHandler<UpdateDriverProfileCommand>,
      ICommandHandler<DeclareVehicleCommand, Guid>,
      ICommandHandler<SubmitDriverDocumentCommand, Guid>,
      ICommandHandler<SubmitDriverDossierCommand>
{
    private readonly IDriverAccountRepository _accounts;
    private readonly IDriverUnitOfWork _unitOfWork;

    public DriverAccountCommandHandler(IDriverAccountRepository accounts, IDriverUnitOfWork unitOfWork)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterDriverCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty)
        {
            return Result.Failure<Guid>(Unauthenticated());
        }

        // CE CONTRÔLE NE SUFFIT PAS SEUL, ET IL N'EST PAS CENSÉ SUFFIRE.
        //
        // Deux inscriptions simultanées du même compte le passent toutes deux
        // (double-clic, réessai du client mobile). C'est l'index unique sur
        // `UserId`, posé par la migration initiale, qui tranche réellement : la
        // seconde écriture est refusée par la base et ressort en 409 par
        // `ServiceExceptionMiddleware`. Ce test-ci n'existe que pour rendre le
        // message utile dans le cas courant, qui est séquentiel.
        if (await _accounts.ExistsForUserAsync(command.UserId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "driver.already_registered", "Un dossier livreur existe déjà pour ce compte."));
        }

        var account = DriverAccount.Register(command.UserId, command.FullName, command.Phone);
        if (account.IsFailure)
        {
            return Result.Failure<Guid>(account.Error);
        }

        await _accounts.AddAsync(account.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return account.Value.Id;
    }

    public async Task<Result> Handle(UpdateDriverProfileCommand command, CancellationToken cancellationToken)
    {
        var account = await ResolveAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(NotFound());
        }

        var updated = account.UpdateProfile(command.FullName, command.Phone);
        if (updated.IsFailure)
        {
            return updated;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<Guid>> Handle(DeclareVehicleCommand command, CancellationToken cancellationToken)
    {
        var account = await ResolveAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<Guid>(NotFound());
        }

        var vehicle = account.DeclareVehicle(
            command.Type, command.Make, command.Model, command.Plate, command.CapacityKg);

        if (vehicle.IsFailure)
        {
            return Result.Failure<Guid>(vehicle.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return vehicle.Value.Id;
    }

    public async Task<Result<Guid>> Handle(SubmitDriverDocumentCommand command, CancellationToken cancellationToken)
    {
        var account = await ResolveAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure<Guid>(NotFound());
        }

        var document = account.SubmitDocument(command.Type, command.ObjectKey);
        if (document.IsFailure)
        {
            return Result.Failure<Guid>(document.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return document.Value.Id;
    }

    public async Task<Result> Handle(SubmitDriverDossierCommand command, CancellationToken cancellationToken)
    {
        var account = await ResolveAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(NotFound());
        }

        var submitted = account.SubmitForReview();
        if (submitted.IsFailure)
        {
            return submitted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private Task<DriverAccount?> ResolveAsync(Guid userId, CancellationToken cancellationToken)
        => userId == Guid.Empty
            ? Task.FromResult<DriverAccount?>(null)
            : _accounts.GetByUserIdAsync(userId, cancellationToken);

    /// <summary>
    /// « Introuvable » et non « interdit » : l'appelant est authentifié mais n'a
    /// pas de dossier. Un 403 laisserait entendre qu'il en existe un qu'on lui
    /// refuse — ce qui est faux, et ce qui l'enverrait vers le support plutôt que
    /// vers le formulaire d'inscription.
    /// </summary>
    private static Error NotFound()
        => Error.NotFound("driver.not_found", "Aucun dossier livreur n'est rattaché à ce compte.");

    private static Error Unauthenticated()
        => Error.Unauthorized("driver.unauthenticated", "Aucun compte dans le jeton présenté.");
}
