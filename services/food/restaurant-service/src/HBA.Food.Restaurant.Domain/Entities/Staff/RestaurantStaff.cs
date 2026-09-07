using HBA.Food.Domain.Staff.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Staff;

public readonly record struct RestaurantStaffId(Guid Value)
{
    public static RestaurantStaffId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Une dérogation nominative : ce membre a, ou n'a pas, cette permission, quel que
/// soit son rôle.
/// </summary>
public sealed class StaffPermissionOverride
{
    private StaffPermissionOverride()
    {
    }

    internal StaffPermissionOverride(FoodPermission permission, bool isGranted)
    {
        Permission = permission;
        IsGranted = isGranted;
    }

    public FoodPermission Permission { get; private set; }

    /// <summary>Vrai = accordée en plus du rôle.</summary>
    public bool IsGranted { get; private set; }

    internal void Set(bool isGranted) => IsGranted = isGranted;
}

/// <summary>UN MEMBRE DU PERSONNEL D'UN RESTAURANT (cahier des charges §8).</summary>
public sealed class RestaurantStaff : AggregateRoot<RestaurantStaffId>
{
    private readonly List<StaffPermissionOverride> _overrides = new();

    private RestaurantStaff()
    {
    }

    private RestaurantStaff(RestaurantStaffId id, Guid restaurantId, Guid userId, StaffRole role, bool isFounder)
        : base(id)
    {
        RestaurantId = restaurantId;
        UserId = userId;
        Role = role;
        IsFounder = isFounder;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid RestaurantId { get; private set; }

    /// <summary>Compte HBA de la personne.</summary>
    public Guid UserId { get; private set; }

    public StaffRole Role { get; private set; }

    /// <summary>Le compte qui a DÉPOSÉ la candidature du restaurant.</summary>
    public bool IsFounder { get; private set; }

    /// <summary>
    /// Un départ se DÉSACTIVE, il ne se supprime pas : le cahier (§21) demande de
    /// tracer les actions sensibles, et un ticket de cuisine accepté par quelqu'un
    /// dont la ligne a disparu n'est plus imputable à personne.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<StaffPermissionOverride> Overrides => _overrides.AsReadOnly();

    /// <summary>
    /// Ce que ce membre peut RÉELLEMENT faire : les défauts de son rôle, plus ses
    /// dérogations accordées, moins ses dérogations retirées.
    /// </summary>
    public IReadOnlySet<FoodPermission> EffectivePermissions
    {
        get
        {
            if (!IsActive)
            {
                return new HashSet<FoodPermission>();
            }

            var effectives = new HashSet<FoodPermission>(FoodPermissions.DefaultsFor(Role));

            foreach (var derogation in _overrides)
            {
                if (derogation.IsGranted)
                {
                    effectives.Add(derogation.Permission);
                }
                else
                {
                    effectives.Remove(derogation.Permission);
                }
            }

            return effectives;
        }
    }

    public bool Has(FoodPermission permission) => EffectivePermissions.Contains(permission);

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>Le fondateur, créé AVEC le restaurant.</summary>
    public static RestaurantStaff Founder(Guid restaurantId, Guid ownerUserId)
        => new(RestaurantStaffId.New(), restaurantId, ownerUserId, StaffRole.Owner, isFounder: true);

    /// <summary>Embauche par un membre habilité.</summary>
    public static Result<RestaurantStaff> Hire(RestaurantStaff actor, Guid userId, StaffRole role)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation("food.staff.user_required", "Compte à rattacher manquant.");
        }

        if (actor.UserId == userId)
        {
            // Il est déjà là, sinon il ne pourrait pas agir.
            return Error.Conflict("food.staff.self", "Vous faites déjà partie de ce restaurant.");
        }

        var habilitation = actor.EnsureCanManageStaff();
        if (habilitation.IsFailure)
        {
            return habilitation.Error;
        }

        var attribution = actor.EnsureCanAssign(role);
        if (attribution.IsFailure)
        {
            return attribution.Error;
        }

        var membre = new RestaurantStaff(RestaurantStaffId.New(), actor.RestaurantId, userId, role, isFounder: false);
        membre.Raise(new StaffHiredDomainEvent(membre.Id.Value, membre.RestaurantId, userId, role.ToString()));

        return membre;
    }

    // ── Mutations, toutes gardées ───────────────────────────────────────────

    public Result ChangeRole(RestaurantStaff actor, StaffRole newRole)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        var attribution = actor.EnsureCanAssign(newRole);
        if (attribution.IsFailure)
        {
            return attribution;
        }

        if (Role == newRole)
        {
            return Result.Success();
        }

        var precedent = Role;
        Role = newRole;

        // LES DÉROGATIONS SONT EFFACÉES AU CHANGEMENT DE RÔLE.
        _overrides.Clear();
        Touch();

        Raise(new StaffRoleChangedDomainEvent(
            Id.Value, RestaurantId, UserId, precedent.ToString(), newRole.ToString()));

        return Result.Success();
    }

    /// <summary>Accorde nommément une permission que le rôle ne donne pas.</summary>
    public Result GrantPermission(RestaurantStaff actor, FoodPermission permission)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        // ON NE DONNE PAS CE QU'ON N'A PAS.
        if (!actor.Has(permission))
        {
            return Result.Failure(Error.Forbidden(
                "food.staff.cannot_delegate",
                $"Vous ne disposez pas vous-même de « {permission.ToCode()} »."));
        }

        SetOverride(permission, isGranted: true);
        Raise(new StaffPermissionChangedDomainEvent(
            Id.Value, RestaurantId, UserId, permission.ToCode(), true));

        return Result.Success();
    }

    /// <summary>Retire nommément une permission que le rôle donne.</summary>
    public Result RevokePermission(RestaurantStaff actor, FoodPermission permission)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        SetOverride(permission, isGranted: false);
        Raise(new StaffPermissionChangedDomainEvent(
            Id.Value, RestaurantId, UserId, permission.ToCode(), false));

        return Result.Success();
    }

    /// <summary>Rend le membre au comportement par défaut de son rôle.</summary>
    public Result ResetPermissions(RestaurantStaff actor)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        _overrides.Clear();
        Touch();

        return Result.Success();
    }

    /// <summary>Le membre quitte le restaurant.</summary>
    public Result Deactivate(RestaurantStaff actor)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        if (!IsActive)
        {
            return Result.Success();
        }

        IsActive = false;
        Touch();

        Raise(new StaffDeactivatedDomainEvent(Id.Value, RestaurantId, UserId));
        return Result.Success();
    }

    public Result Reactivate(RestaurantStaff actor)
    {
        var garde = EnsureCanAdminister(actor);
        if (garde.IsFailure)
        {
            return garde;
        }

        if (IsActive)
        {
            return Result.Success();
        }

        IsActive = true;
        Touch();

        Raise(new StaffReactivatedDomainEvent(Id.Value, RestaurantId, UserId, Role.ToString()));
        return Result.Success();
    }

    // ── Les gardes ──────────────────────────────────────────────────────────

    /// <summary>L'ACTEUR A-T-IL LE DROIT D'AGIR SUR CE MEMBRE-CI ?</summary>
    private Result EnsureCanAdminister(RestaurantStaff actor)
    {
        // 1. LE CLOISONNEMENT PAR RESTAURANT (§20 du cahier).
        if (actor.RestaurantId != RestaurantId)
        {
            return Result.Failure(Error.NotFound("food.staff.not_found", "Membre introuvable."));
        }

        if (!actor.IsActive)
        {
            return Result.Failure(Error.Forbidden(
                "food.staff.inactive", "Votre accès à ce restaurant est désactivé."));
        }

        var habilitation = actor.EnsureCanManageStaff();
        if (habilitation.IsFailure)
        {
            return habilitation;
        }

        // 2. ON NE S'ADMINISTRE PAS SOI-MÊME.
        if (actor.Id == Id)
        {
            return Result.Failure(Error.Forbidden(
                "food.staff.self", "On ne modifie pas ses propres droits."));
        }

        // 3. LE FONDATEUR EST INTOUCHABLE.
        if (IsFounder)
        {
            return Result.Failure(Error.Forbidden(
                "food.staff.founder",
                "Le compte à l'origine de l'établissement ne peut être ni rétrogradé ni désactivé."));
        }

        // 4. LA HIÉRARCHIE. Strictement plus haut, sauf entre propriétaires.
        var acteurEstProprietaire = actor.Role == StaffRole.Owner;
        var cibleEstProprietaire = Role == StaffRole.Owner;

        if (actor.Role >= Role && !(acteurEstProprietaire && cibleEstProprietaire))
        {
            return Result.Failure(Error.Forbidden(
                "food.staff.rank",
                "Vous ne pouvez agir que sur des membres d'un rang inférieur au vôtre."));
        }

        return Result.Success();
    }

    private Result EnsureCanManageStaff()
        => Has(FoodPermission.StaffManage)
            ? Result.Success()
            : Result.Failure(Error.Forbidden(
                "food.staff.forbidden", "Vous n'êtes pas habilité à gérer le personnel."));

    /// <summary>Peut-on attribuer CE rôle ?</summary>
    private Result EnsureCanAssign(StaffRole role)
    {
        if (Role < role || (Role == StaffRole.Owner && role == StaffRole.Owner))
        {
            return Result.Success();
        }

        return Result.Failure(Error.Forbidden(
            "food.staff.rank",
            $"Vous ne pouvez pas attribuer le rôle « {role} »."));
    }

    private void SetOverride(FoodPermission permission, bool isGranted)
    {
        var existante = _overrides.FirstOrDefault(o => o.Permission == permission);

        if (existante is null)
        {
            _overrides.Add(new StaffPermissionOverride(permission, isGranted));
        }
        else
        {
            existante.Set(isGranted);
        }

        Touch();
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>Accès au personnel des restaurants.</summary>
public interface IRestaurantStaffRepository
{
    Task<RestaurantStaff?> GetByIdAsync(RestaurantStaffId id, CancellationToken cancellationToken = default);

    /// <summary>L'appartenance d'un compte à un restaurant donné.</summary>
    Task<RestaurantStaff?> GetMembershipAsync(
        Guid restaurantId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Le restaurant où ce compte travaille.</summary>
    Task<RestaurantStaff?> GetActiveMembershipByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RestaurantStaff>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Combien de propriétaires ACTIFS reste-t-il ? Voir la garde du dernier
    /// propriétaire.
    /// </summary>
    Task<int> CountActiveOwnersAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task AddAsync(RestaurantStaff staff, CancellationToken cancellationToken = default);
}
