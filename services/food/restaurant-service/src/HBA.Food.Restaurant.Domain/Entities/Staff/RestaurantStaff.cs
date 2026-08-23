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
/// Une dérogation nominative : ce membre a, ou n'a pas, cette permission, quel
/// que soit son rôle.
///
/// LE « NON » EST STOCKÉ AUTANT QUE LE « OUI ». Un retrait ne peut pas se
/// représenter par une absence : l'absence signifie déjà « ce que le rôle
/// prévoit ». Retirer <c>OrderAccept</c> à un caissier en particulier demande de
/// l'écrire.
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

    /// <summary>Vrai = accordée en plus du rôle. Faux = retirée malgré le rôle.</summary>
    public bool IsGranted { get; private set; }

    internal void Set(bool isGranted) => IsGranted = isGranted;
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MEMBRE DU PERSONNEL D'UN RESTAURANT (cahier des charges §8).
///
/// Avant ce type, un restaurant n'avait qu'UNE seule personne : le compte qui
/// l'avait créé. Pas de manager, pas de caissier, pas de cuisinier — et donc
/// aucune des routes de commande et de cuisine que décrit le cahier ne pouvait
/// être écrite correctement, puisqu'aucune ne savait QUI agissait.
///
/// CE MODULE NE STOCKE AUCUNE DONNÉE D'AUTHENTIFICATION.
///
/// <c>UserId</c> vient d'Identity et n'est qu'une référence. Le cahier l'exige
/// (§8) et la frontière du module l'impose : Food ne référence aucun autre
/// module, pas même ses contrats.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SIGNATURE DES MUTATIONS EXIGE UN ACTEUR, ET C'EST DÉLIBÉRÉ.
///
/// <c>ChangeRole</c>, <c>GrantPermission</c>, <c>Deactivate</c> — aucune ne peut
/// être appelée sans nommer QUI agit. C'est la leçon directe du défaut F7 :
/// <c>LiftSuspension</c> s'appuyait sur un invariant garanti ailleurs, et rien
/// n'empêchait de contourner l'endroit qui le tenait.
///
/// Ici, la garde ne peut pas être oubliée parce qu'il n'existe aucune surcharge
/// sans acteur. Le compilateur refuse l'appel non gardé.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Compte HBA de la personne. Vient d'Identity — jamais créé ici.</summary>
    public Guid UserId { get; private set; }

    public StaffRole Role { get; private set; }

    /// <summary>
    /// Le compte qui a DÉPOSÉ la candidature du restaurant.
    ///
    /// INTOUCHABLE : ni rétrogradable, ni désactivable, même par un autre
    /// propriétaire. C'est la clé de dernier recours. Sans elle, deux copropriétaires
    /// en conflit peuvent se désactiver mutuellement, et l'établissement se retrouve
    /// sans personne pour y entrer — un incident que seul un accès direct à la base
    /// permettrait de réparer.
    /// </summary>
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
    ///
    /// Un membre désactivé ne peut plus rien : la porte est fermée avant qu'on ne
    /// regarde le trousseau.
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

    /// <summary>
    /// Le fondateur, créé AVEC le restaurant.
    ///
    /// SEULE CRÉATION SANS ACTEUR DE TOUT CE TYPE, et elle n'a qu'un appelant :
    /// l'enregistrement d'un établissement. À cet instant il n'existe encore
    /// personne pour autoriser quoi que ce soit — exiger un acteur rendrait le
    /// premier membre impossible à créer.
    ///
    /// Sans cet amorçage, un restaurant naîtrait sans personnel : son propriétaire
    /// ne pourrait ni gérer sa carte, ni embaucher, ni entrer dans son propre
    /// espace.
    /// </summary>
    public static RestaurantStaff Founder(Guid restaurantId, Guid ownerUserId)
        => new(RestaurantStaffId.New(), restaurantId, ownerUserId, StaffRole.Owner, isFounder: true);

    /// <summary>
    /// Embauche par un membre habilité.
    ///
    /// L'unicité de <c>UserId</c> dans un restaurant n'est PAS vérifiée ici — elle
    /// porte sur l'ensemble du personnel, que cet agrégat ne voit pas. Elle est
    /// tenue par l'appelant et par un index unique en base.
    /// </summary>
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
        //
        // Elles ont été accordées à un caissier, en connaissance de son périmètre.
        // Les laisser survivre à un passage en cuisine donnerait à un cuisinier
        // l'accès au chiffre d'affaires — l'interdiction la plus explicite du
        // cahier (§2) — sans que personne ne l'ait décidé.
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
        //
        // Sans cette ligne, un manager s'inventerait un adjoint doté de
        // « restaurant.settings.manage », puis se ferait accorder la même chose
        // par lui. L'escalade ne demanderait pas deux minutes, et aucune trace ne
        // dirait qu'elle a eu lieu — chaque geste, pris seul, serait légitime.
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

    /// <summary>
    /// Retire nommément une permission que le rôle donne.
    ///
    /// N'exige PAS que l'acteur détienne lui-même la permission : retirer réduit
    /// le privilège, et l'exiger empêcherait un propriétaire de reprendre un droit
    /// qu'il s'est lui-même retiré.
    /// </summary>
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

    /// <summary>
    /// Le membre quitte le restaurant.
    ///
    /// L'INVARIANT « AU MOINS UN PROPRIÉTAIRE ACTIF » N'EST PAS VÉRIFIÉ ICI :
    /// il porte sur l'ENSEMBLE du personnel, que cet agrégat ne voit pas. Il est
    /// tenu par le gestionnaire de commande, qui compte avant de désactiver — même
    /// forme que la garde de suppression d'une section garnie.
    /// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ACTEUR A-T-IL LE DROIT D'AGIR SUR CE MEMBRE-CI ?
    ///
    /// Cinq conditions, chacune fermant une porte différente. Elles sont dans
    /// l'ordre du moins au plus révélateur : on ne dit pas « ce membre existe mais
    /// vous êtes trop bas » à quelqu'un qui n'est même pas du restaurant.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private Result EnsureCanAdminister(RestaurantStaff actor)
    {
        // 1. LE CLOISONNEMENT PAR RESTAURANT (§20 du cahier).
        //
        // L'identifiant du membre vient du client. Sans cette comparaison, un
        // manager licencierait le personnel d'un concurrent avec un simple GUID.
        // « Introuvable » et non « interdit » : distinguer les deux dirait à qui
        // teste des identifiants lesquels existent.
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
        //
        // Dans un sens cela évite l'auto-promotion, dans l'autre l'auto-exclusion
        // accidentelle — un propriétaire qui se retire « staff.manage » ne peut
        // plus se le rendre.
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
        //
        // Sans elle, un manager rétrograderait le propriétaire et prendrait la
        // main sur l'établissement. L'exception entre propriétaires est nécessaire :
        // un associé parti doit pouvoir être retiré par l'autre — et le fondateur
        // reste protégé par la condition précédente.
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

    /// <summary>
    /// Peut-on attribuer CE rôle ?
    ///
    /// Strictement inférieur au sien, sauf qu'un propriétaire peut en nommer un
    /// autre. Autoriser l'égalité ailleurs laisserait un manager se cloner : deux
    /// pairs dont aucun ne peut plus agir sur l'autre, et un privilège qui se
    /// multiplie sans jamais franchir de palier.
    /// </summary>
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

    /// <summary>
    /// Le restaurant où ce compte travaille.
    ///
    /// UN COMPTE, UN RESTAURANT. C'est ce qui permet aux routes de l'espace
    /// restaurateur de résoudre l'établissement DEPUIS LE JETON, sans identifiant
    /// falsifiable dans l'URL. Le jour où un cuisinier travaillera dans deux
    /// maquis, il faudra un identifiant explicite et cette méthode disparaîtra.
    /// </summary>
    Task<RestaurantStaff?> GetActiveMembershipByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RestaurantStaff>> ListByRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    /// <summary>Combien de propriétaires ACTIFS reste-t-il ? Voir la garde du dernier propriétaire.</summary>
    Task<int> CountActiveOwnersAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task AddAsync(RestaurantStaff staff, CancellationToken cancellationToken = default);
}
