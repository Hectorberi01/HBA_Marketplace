using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Staff;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Staff;

// TOUTES CES COMMANDES PORTENT L'ACTEUR, ET IL VIENT DU JETON.

public sealed record HireStaffCommand(
    Guid RestaurantId, Guid ActorUserId, Guid UserId, StaffRole Role) : ICommand<Guid>;

public sealed record ChangeStaffRoleCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId, StaffRole Role) : ICommand;

public sealed record GrantStaffPermissionCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId, FoodPermission Permission) : ICommand;

public sealed record RevokeStaffPermissionCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId, FoodPermission Permission) : ICommand;

public sealed record ResetStaffPermissionsCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId) : ICommand;

public sealed record DeactivateStaffCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId) : ICommand;

public sealed record ReactivateStaffCommand(
    Guid RestaurantId, Guid ActorUserId, Guid StaffId) : ICommand;

internal sealed class StaffCommandHandler
    : ICommandHandler<HireStaffCommand, Guid>,
      ICommandHandler<ChangeStaffRoleCommand>,
      ICommandHandler<GrantStaffPermissionCommand>,
      ICommandHandler<RevokeStaffPermissionCommand>,
      ICommandHandler<ResetStaffPermissionsCommand>,
      ICommandHandler<DeactivateStaffCommand>,
      ICommandHandler<ReactivateStaffCommand>
{
    private readonly IRestaurantStaffRepository _staff;
    private readonly IFoodUnitOfWork _unitOfWork;

    public StaffCommandHandler(IRestaurantStaffRepository staff, IFoodUnitOfWork unitOfWork)
    {
        _staff = staff;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(HireStaffCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _staff.GetMembershipAsync(command.RestaurantId, command.ActorUserId, cancellationToken);
        if (acteur is null)
        {
            return Result.Failure<Guid>(NotAMember);
        }

        // UN COMPTE NE FIGURE QU'UNE FOIS DANS UN RESTAURANT.
        var existant = await _staff.GetMembershipAsync(command.RestaurantId, command.UserId, cancellationToken);
        if (existant is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.staff.already_member",
                existant.IsActive
                    ? "Ce compte fait déjà partie du personnel."
                    : "Ce compte a déjà travaillé ici : réactivez-le plutôt que de le recréer."));
        }

        var membre = RestaurantStaff.Hire(acteur, command.UserId, command.Role);
        if (membre.IsFailure)
        {
            return Result.Failure<Guid>(membre.Error);
        }

        await _staff.AddAsync(membre.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return membre.Value.Id.Value;
    }

    public async Task<Result> Handle(ChangeStaffRoleCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        var (acteur, cible) = paire.Value;

        // LA GARDE DU DERNIER PROPRIÉTAIRE, PREMIER VERSANT.
        if (cible.Role == StaffRole.Owner && command.Role != StaffRole.Owner)
        {
            var restants = await _staff.CountActiveOwnersAsync(command.RestaurantId, cancellationToken);
            if (restants <= 1)
            {
                return Result.Failure(DernierProprietaire);
            }
        }

        var result = cible.ChangeRole(acteur, command.Role);
        return await CommitAsync(result, cancellationToken);
    }

    public async Task<Result> Handle(GrantStaffPermissionCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        return await CommitAsync(
            paire.Value.Target.GrantPermission(paire.Value.Actor, command.Permission), cancellationToken);
    }

    public async Task<Result> Handle(RevokeStaffPermissionCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        return await CommitAsync(
            paire.Value.Target.RevokePermission(paire.Value.Actor, command.Permission), cancellationToken);
    }

    public async Task<Result> Handle(ResetStaffPermissionsCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        return await CommitAsync(paire.Value.Target.ResetPermissions(paire.Value.Actor), cancellationToken);
    }

    public async Task<Result> Handle(DeactivateStaffCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        var (acteur, cible) = paire.Value;

        // LA GARDE DU DERNIER PROPRIÉTAIRE, SECOND VERSANT. Rétrograder et
        // désactiver mènent au même établissement orphelin ; oublier l'un des deux
        // chemins ne fermerait que la moitié de la porte.
        if (cible.Role == StaffRole.Owner && cible.IsActive)
        {
            var restants = await _staff.CountActiveOwnersAsync(command.RestaurantId, cancellationToken);
            if (restants <= 1)
            {
                return Result.Failure(DernierProprietaire);
            }
        }

        return await CommitAsync(cible.Deactivate(acteur), cancellationToken);
    }

    public async Task<Result> Handle(ReactivateStaffCommand command, CancellationToken cancellationToken)
    {
        var paire = await LoadAsync(command.RestaurantId, command.ActorUserId, command.StaffId, cancellationToken);
        if (paire.IsFailure)
        {
            return Result.Failure(paire.Error);
        }

        return await CommitAsync(paire.Value.Target.Reactivate(paire.Value.Actor), cancellationToken);
    }

    // ── Chargement ──────────────────────────────────────────────────────────

    private static readonly Error NotAMember = Error.Forbidden(
        "food.staff.not_member", "Vous ne faites pas partie du personnel de cet établissement.");

    private static readonly Error DernierProprietaire = Error.Conflict(
        "food.staff.last_owner",
        "C'est le dernier propriétaire actif de l'établissement. "
        + "Nommez-en un autre avant de retirer celui-ci.");

    /// <summary>Charge l'acteur ET la cible, tous deux rattachés au même restaurant.</summary>
    private async Task<Result<(RestaurantStaff Actor, RestaurantStaff Target)>> LoadAsync(
        Guid restaurantId, Guid actorUserId, Guid staffId, CancellationToken cancellationToken)
    {
        var acteur = await _staff.GetMembershipAsync(restaurantId, actorUserId, cancellationToken);
        if (acteur is null)
        {
            return NotAMember;
        }

        var cible = await _staff.GetByIdAsync(new RestaurantStaffId(staffId), cancellationToken);
        if (cible is null || cible.RestaurantId != restaurantId)
        {
            return Error.NotFound("food.staff.not_found", "Membre introuvable.");
        }

        return (acteur, cible);
    }

    private async Task<Result> CommitAsync(Result result, CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
