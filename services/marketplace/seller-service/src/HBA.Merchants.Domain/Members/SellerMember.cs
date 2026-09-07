using HBA.Merchants.Domain.Members.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Members;

/// <summary>Identité forte d'un membre.</summary>
public readonly record struct SellerMemberId(Guid Value)
{
    public static SellerMemberId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statuts du §5. <see cref="Suspended"/> conserve l'historique et les affectations
/// mais interdit immédiatement l'accès ; <see cref="Revoked"/> est une révocation
/// administrative ; <see cref="Left"/> un départ volontaire.
/// </summary>
public enum MemberStatus
{
    /// <summary>ÉTAT INATTEIGNABLE, ET IL NE FAUT SURTOUT PAS LE RETIRER (lot 9.2).</summary>
    Invited = 0,
    Active = 1,
    Suspended = 2,
    Revoked = 3,
    Left = 4
}

/// <summary>Statut d'une affectation à une boutique.</summary>
public enum StoreMembershipStatus
{
    Active = 0,
    Suspended = 1
}

/// <summary>Où en est l'application du cadrage par boutique, pour CETTE affectation (§8).</summary>
public enum StoreEnforcement
{
    Prepared = 0,

    /// <summary>
    /// ÉTAT INATTEIGNABLE : la moitié « appliquée » n'a jamais été branchée (lot
    /// 9.2).
    /// </summary>
    Enforced = 1
}

/// <summary>
/// Un rôle porté par un membre au niveau du vendeur — une ligne de <c>
/// seller_member_roles</c>.
/// </summary>
public sealed class SellerMemberRole
{
    private SellerMemberRole()
    {
    }

    internal SellerMemberRole(SellerRoleId roleId) => RoleId = roleId;

    public SellerRoleId RoleId { get; private set; }
}

/// <summary>
/// Un rôle porté sur une boutique précise — une ligne de <c>
/// store_membership_roles</c>.
/// </summary>
public sealed class StoreMembershipRole
{
    private StoreMembershipRole()
    {
    }

    internal StoreMembershipRole(SellerRoleId roleId) => RoleId = roleId;

    public SellerRoleId RoleId { get; private set; }
}

/// <summary>Affectation d'un membre à une boutique, avec ses rôles.</summary>
public sealed class StoreMembership
{
    private readonly List<StoreMembershipRole> _roles = [];

    private StoreMembership()
    {
    }

    internal StoreMembership(Guid storeId, IEnumerable<SellerRoleId> roleIds)
    {
        Id = Guid.NewGuid();
        StoreId = storeId;
        Status = StoreMembershipStatus.Active;
        Enforcement = StoreEnforcement.Prepared;
        CreatedOnUtc = DateTime.UtcNow;
        _roles.AddRange(roleIds.Distinct().Select(id => new StoreMembershipRole(id)));
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    public StoreMembershipStatus Status { get; private set; }

    public StoreEnforcement Enforcement { get; private set; }

    public IReadOnlyCollection<SellerRoleId> RoleIds => [.. _roles.Select(r => r.RoleId)];

    internal void SetRoles(IEnumerable<SellerRoleId> roleIds)
    {
        _roles.Clear();
        _roles.AddRange(roleIds.Distinct().Select(id => new StoreMembershipRole(id)));
        UpdatedOnUtc = DateTime.UtcNow;
    }

    internal void Suspend()
    {
        Status = StoreMembershipStatus.Suspended;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    internal void Reactivate()
    {
        Status = StoreMembershipStatus.Active;
        UpdatedOnUtc = DateTime.UtcNow;
    }
}

/// <summary>UN MEMBRE DE L'ÉQUIPE D'UN VENDEUR.</summary>
public sealed class SellerMember : AggregateRoot<SellerMemberId>
{
    private readonly List<SellerMemberRole> _sellerRoles = [];
    private readonly List<StoreMembership> _storeMemberships = [];

    private SellerMember()
    {
    }

    private SellerMember(
        SellerMemberId id, Guid sellerId, Guid userId, MemberStatus status,
        string? displayName, string? jobTitle, Guid? invitedByUserId)
        : base(id)
    {
        SellerId = sellerId;
        UserId = userId;
        Status = status;
        DisplayName = displayName;
        JobTitle = jobTitle;
        InvitedByUserId = invitedByUserId;
        CreatedOnUtc = DateTime.UtcNow;

        if (status == MemberStatus.Active)
        {
            JoinedOnUtc = CreatedOnUtc;
        }
    }

    public Guid SellerId { get; private set; }

    /// <summary>Le compte Identity. Jamais créé ici, jamais modifié ici.</summary>
    public Guid UserId { get; private set; }

    public MemberStatus Status { get; private set; }

    public string? DisplayName { get; private set; }

    public string? JobTitle { get; private set; }

    public Guid? InvitedByUserId { get; private set; }

    public DateTime? JoinedOnUtc { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    public IReadOnlyCollection<SellerRoleId> SellerRoleIds => [.. _sellerRoles.Select(r => r.RoleId)];

    public IReadOnlyCollection<StoreMembership> StoreMemberships => _storeMemberships.AsReadOnly();

    /// <summary>Seul un membre <see cref="MemberStatus.Active"/> peut agir.</summary>
    public bool CanAct => Status == MemberStatus.Active;

    /// <summary>
    /// Porteur du rôle système OWNER — une comparaison d'identifiant, pas une
    /// requête.
    /// </summary>
    public bool IsOwner => _sellerRoles.Any(r => r.RoleId == SystemSellerRoles.OwnerId);

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>Le propriétaire, créé par l'inscription du vendeur ou par la reprise.</summary>
    internal static SellerMember Owner(Guid sellerId, Guid ownerUserId)
    {
        var membre = new SellerMember(
            SellerMemberId.New(), sellerId, ownerUserId, MemberStatus.Active,
            displayName: null, jobTitle: null, invitedByUserId: null);

        membre._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.OwnerId));
        return membre;
    }

    /// <summary>Un membre rattaché après acceptation d'une invitation.</summary>
    /// <param name="acteur">Celui qui a émis l'invitation, revérifié à l'acceptation.</param>
    public static Result<SellerMember> Join(
        MemberActor acteur,
        Guid userId,
        string? displayName,
        string? jobTitle,
        IReadOnlyCollection<SellerRole> rolesVendeur,
        IReadOnlyCollection<(Guid StoreId, IReadOnlyCollection<SellerRole> Roles)> affectations)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation("sellers.member.user_required", "Compte à rattacher manquant.");
        }

        if (acteur.UserId == userId)
        {
            return Error.Conflict("sellers.member.self", "Vous faites déjà partie de ce vendeur.");
        }

        var habilitation = acteur.Ensure(MerchantPermission.MemberInvite);
        if (habilitation.IsFailure)
        {
            return habilitation.Error;
        }

        // DEUX PÉRIMÈTRES, DEUX CONTRÔLES — ET NON UN SEUL SUR L'UNION.
        var delegation = EnsureCanAssign(acteur, rolesVendeur);
        if (delegation.IsFailure)
        {
            return delegation.Error;
        }

        foreach (var affectation in affectations)
        {
            var parBoutique = EnsureCanAssign(acteur, affectation.Roles, affectation.StoreId);
            if (parBoutique.IsFailure)
            {
                return parBoutique.Error;
            }
        }

        var membre = new SellerMember(
            SellerMemberId.New(), acteur.SellerId, userId, MemberStatus.Active,
            Trim(displayName, 150), Trim(jobTitle, 120), acteur.UserId);

        membre._sellerRoles.AddRange(
            rolesVendeur.Select(r => r.Id).Distinct().Select(id => new SellerMemberRole(id)));

        foreach (var (storeId, roles) in affectations)
        {
            membre._storeMemberships.Add(new StoreMembership(storeId, roles.Select(r => r.Id)));
        }

        membre.Raise(new SellerMemberJoinedDomainEvent(
            membre.Id.Value, membre.SellerId, userId,
            [.. membre._sellerRoles.Select(r => r.RoleId.Value)],
            [.. membre._storeMemberships.Select(s => s.StoreId)]));

        return membre;
    }

    /// <summary>Le membre né d'une invitation acceptée.</summary>
    internal static Result<SellerMember> FromInvitation(
        SellerInvitation invitation,
        IReadOnlyCollection<SellerRole> rolesVendeur,
        IReadOnlyCollection<(Guid StoreId, IReadOnlyCollection<SellerRole> Roles)> affectations)
    {
        if (invitation.Status != InvitationStatus.Accepted
            || invitation.AcceptedByUserId is not { } userId)
        {
            return Error.Conflict(
                "sellers.invitation.not_accepted", "Cette invitation n'a pas été acceptée.");
        }

        var membre = new SellerMember(
            SellerMemberId.New(), invitation.SellerId, userId, MemberStatus.Active,
            invitation.DisplayName, invitation.JobTitle, invitation.InvitedByUserId);

        membre._sellerRoles.AddRange(
            rolesVendeur.Select(r => r.Id).Distinct().Select(id => new SellerMemberRole(id)));

        foreach (var (storeId, roles) in affectations)
        {
            membre._storeMemberships.Add(new StoreMembership(storeId, roles.Select(r => r.Id)));
        }

        membre.Raise(new SellerMemberJoinedDomainEvent(
            membre.Id.Value, membre.SellerId, userId,
            [.. membre._sellerRoles.Select(r => r.RoleId.Value)],
            [.. membre._storeMemberships.Select(s => s.StoreId)]));

        return membre;
    }

    // ── Mutations, toutes gardées ───────────────────────────────────────────

    /// <summary>
    /// LE TRANSFERT DE PROPRIÉTÉ — L'OPÉRATION QUE TROIS GARDES RÉCLAMAIENT SANS
    /// QU'ELLE EXISTE (ISSUE-040).
    /// </summary>
    /// <param name="cedant">Le membre qui porte aujourd'hui le rôle OWNER.</param>
    /// <param name="beneficiaire">Le membre qui le recevra.</param>
    /// <param name="acteur">L'appelant, résolu depuis le jeton.</param>
    public static Result TransferOwnership(
        SellerMember cedant, SellerMember beneficiaire, MemberActor acteur)
    {
        if (!acteur.CanAct)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.actor_inactive", "Votre appartenance à ce vendeur n'est pas active."));
        }

        if (cedant.SellerId != acteur.SellerId || beneficiaire.SellerId != acteur.SellerId)
        {
            // Même réponse que « membre inexistant » : dire qu'il existe ailleurs
            // renseignerait sur l'équipe d'un autre commerçant.
            return Result.Failure(Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        if (!acteur.IsOwner || !acteur.Has(MerchantPermission.OwnershipTransfer))
        {
            return Result.Failure(Error.Forbidden(
                "sellers.ownership.forbidden",
                "Seul le propriétaire du dossier peut transférer la propriété."));
        }

        if (acteur.Id != cedant.Id)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.ownership.not_yours",
                "On ne transfère que sa propre propriété, jamais celle d'un autre propriétaire."));
        }

        if (!cedant.IsOwner)
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.not_owner", "Ce membre ne porte pas le rôle de propriétaire."));
        }

        if (beneficiaire.Id == cedant.Id)
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.already_owner", "Ce membre est déjà propriétaire du dossier."));
        }

        // LE BÉNÉFICIAIRE DOIT ÊTRE ACTIF, PAS SEULEMENT EXISTANT.
        if (!beneficiaire.CanAct)
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.recipient_inactive",
                "Le nouveau propriétaire doit être un membre actif."));
        }

        if (beneficiaire.IsOwner)
        {
            return Result.Failure(Error.Conflict(
                "sellers.ownership.already_owner", "Ce membre est déjà propriétaire du dossier."));
        }

        cedant._sellerRoles.RemoveAll(r => r.RoleId == SystemSellerRoles.OwnerId);
        cedant.Touch();

        // LE CÉDANT NE RESTE PAS SANS RÔLE.
        if (cedant._sellerRoles.Count == 0)
        {
            cedant._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.SellerAdminId));
        }

        beneficiaire._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.OwnerId));
        beneficiaire.Touch();

        // LEVÉ SUR LE BÉNÉFICIAIRE, PAS SUR LE CÉDANT.
        beneficiaire.Raise(new SellerOwnershipTransferredDomainEvent(
            acteur.SellerId,
            cedant.Id.Value, cedant.UserId,
            beneficiaire.Id.Value, beneficiaire.UserId));

        return Result.Success();
    }

    public Result SetSellerRoles(MemberActor acteur, IReadOnlyCollection<SellerRole> roles)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberAssignRole);
        if (garde.IsFailure)
        {
            return garde;
        }

        // ON NE SE DÉPOUILLE PAS DU RÔLE DE PROPRIÉTAIRE PAR CE CHEMIN.
        if (IsOwner && !roles.Any(r => r.Id == SystemSellerRoles.OwnerId))
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.owner_role_locked",
                "Le rôle de propriétaire se transfère, il ne se retire pas."));
        }

        // OWNER EST RETIRÉ DE LA DÉLÉGATION QUAND LA CIBLE LE PORTE DÉJÀ.
        var aDeleguer = IsOwner
            ? roles.Where(r => r.Id != SystemSellerRoles.OwnerId).ToArray()
            : roles;

        var delegation = EnsureCanAssign(acteur, aDeleguer);
        if (delegation.IsFailure)
        {
            return delegation;
        }

        _sellerRoles.Clear();
        _sellerRoles.AddRange(roles.Select(r => r.Id).Distinct().Select(id => new SellerMemberRole(id)));
        Touch();

        Raise(new SellerMemberRolesChangedDomainEvent(
            Id.Value, SellerId, UserId, [.. _sellerRoles.Select(r => r.RoleId.Value)]));

        return Result.Success();
    }

    public Result AssignStore(MemberActor acteur, Guid storeId, IReadOnlyCollection<SellerRole> roles)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberAssignStore);
        if (garde.IsFailure)
        {
            return garde;
        }

        // MESURÉE DANS LA BOUTIQUE VISÉE, PAS SUR L'UNION DE L'ACTEUR.
        var delegation = EnsureCanAssign(acteur, roles, storeId);
        if (delegation.IsFailure)
        {
            return delegation;
        }

        var existante = _storeMemberships.FirstOrDefault(s => s.StoreId == storeId);

        if (existante is null)
        {
            _storeMemberships.Add(new StoreMembership(storeId, roles.Select(r => r.Id)));
        }
        else
        {
            existante.SetRoles(roles.Select(r => r.Id));
        }

        Touch();
        Raise(new SellerMemberStoreAssignedDomainEvent(Id.Value, SellerId, UserId, storeId));

        return Result.Success();
    }

    public Result UnassignStore(MemberActor acteur, Guid storeId)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberAssignStore);
        if (garde.IsFailure)
        {
            return garde;
        }

        var affectation = _storeMemberships.FirstOrDefault(s => s.StoreId == storeId);
        if (affectation is null)
        {
            return Result.Failure(Error.NotFound(
                "sellers.member.store_not_assigned", "Ce membre n'est pas affecté à cette boutique."));
        }

        _storeMemberships.Remove(affectation);
        Touch();
        Raise(new SellerMemberStoreUnassignedDomainEvent(Id.Value, SellerId, UserId, storeId));

        return Result.Success();
    }

    /// <summary>Suspend l'accès sans rien effacer (§5).</summary>
    public Result Suspend(MemberActor acteur, bool estDernierProprietaire)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberSuspend);
        if (garde.IsFailure)
        {
            return garde;
        }

        var proprietaire = EnsureNotLastOwner(estDernierProprietaire);
        if (proprietaire.IsFailure)
        {
            return proprietaire;
        }

        if (Status == MemberStatus.Suspended)
        {
            return Result.Success();
        }

        Status = MemberStatus.Suspended;
        Touch();

        Raise(new SellerMemberSuspendedDomainEvent(Id.Value, SellerId, UserId));
        return Result.Success();
    }

    public Result Reactivate(MemberActor acteur)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberSuspend);
        if (garde.IsFailure)
        {
            return garde;
        }

        // ON NE RESSUSCITE PAS UN ACCÈS RÉVOQUÉ.
        if (Status is MemberStatus.Revoked or MemberStatus.Left)
        {
            return Result.Failure(Error.Conflict(
                "sellers.member.not_reactivable",
                "Un accès révoqué ou quitté se rouvre par une nouvelle invitation."));
        }

        if (Status == MemberStatus.Active)
        {
            return Result.Success();
        }

        Status = MemberStatus.Active;
        JoinedOnUtc ??= DateTime.UtcNow;
        Touch();

        Raise(new SellerMemberActivatedDomainEvent(Id.Value, SellerId, UserId));
        return Result.Success();
    }

    /// <param name="aUneAutreAppartenance">
    /// Le compte appartient-il encore à une autre équipe vendeur ? Compté par
    /// l'appelant AVANT la mutation — voir l'encadré de
    /// <see cref="SellerMemberRevokedDomainEvent"/> , qui explique pourquoi un
    /// gestionnaire d'événement ne peut pas le calculer lui-même.
    /// </param>
    public Result Revoke(MemberActor acteur, bool estDernierProprietaire, bool aUneAutreAppartenance)
    {
        var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberRevoke);
        if (garde.IsFailure)
        {
            return garde;
        }

        var proprietaire = EnsureNotLastOwner(estDernierProprietaire);
        if (proprietaire.IsFailure)
        {
            return proprietaire;
        }

        if (Status == MemberStatus.Revoked)
        {
            return Result.Success();
        }

        Status = MemberStatus.Revoked;
        Touch();

        Raise(new SellerMemberRevokedDomainEvent(Id.Value, SellerId, UserId, aUneAutreAppartenance));
        return Result.Success();
    }

    /// <summary>Départ volontaire — le seul geste qu'un membre pose sur lui-même.</summary>
    public Result Leave(bool estDernierProprietaire, bool aUneAutreAppartenance)
    {
        var proprietaire = EnsureNotLastOwner(estDernierProprietaire);
        if (proprietaire.IsFailure)
        {
            return proprietaire;
        }

        if (Status is MemberStatus.Left or MemberStatus.Revoked)
        {
            return Result.Success();
        }

        Status = MemberStatus.Left;
        Touch();

        Raise(new SellerMemberRevokedDomainEvent(Id.Value, SellerId, UserId, aUneAutreAppartenance));
        return Result.Success();
    }

    public Result UpdateProfile(MemberActor acteur, string? displayName, string? jobTitle)
    {
        // LE CHEMIN « SOI-MÊME » NE VÉRIFIAIT NI L'ACTIVITÉ NI LE VENDEUR.
        if (acteur.SellerId != SellerId || !acteur.CanAct)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.not_active", "Votre accès à ce vendeur n'est pas actif."));
        }

        // Un membre corrige sa propre fiche ; sinon il faut l'habilitation.
        if (acteur.Id != Id)
        {
            var garde = EnsureCanAdminister(acteur, MerchantPermission.MemberAssignRole);
            if (garde.IsFailure)
            {
                return garde;
            }
        }

        DisplayName = Trim(displayName, 150);
        JobTitle = Trim(jobTitle, 120);
        Touch();

        return Result.Success();
    }

    // ── Les permissions effectives ──────────────────────────────────────────

    /// <summary>EN PHASE 1, LES RÔLES DE BOUTIQUE COMPTENT AU NIVEAU DU VENDEUR.</summary>
    public IReadOnlySet<MerchantPermission> EffectivePermissions(IReadOnlyCollection<SellerRole> roles)
    {
        if (!CanAct)
        {
            return new HashSet<MerchantPermission>();
        }

        var parId = roles.ToDictionary(r => r.Id);
        var effectives = new HashSet<MerchantPermission>();

        foreach (var porte in _sellerRoles)
        {
            if (parId.TryGetValue(porte.RoleId, out var role))
            {
                effectives.UnionWith(role.Permissions);
            }
        }

        foreach (var affectation in _storeMemberships.Where(s => s.Status == StoreMembershipStatus.Active))
        {
            foreach (var roleId in affectation.RoleIds)
            {
                if (parId.TryGetValue(roleId, out var role))
                {
                    effectives.UnionWith(role.Permissions);
                }
            }
        }

        return effectives;
    }

    /// <summary>
    /// Les permissions portées par les rôles attribués AU NIVEAU DU VENDEUR, sans
    /// aucune affectation boutique.
    /// </summary>
    public IReadOnlySet<MerchantPermission> SellerLevelPermissions(IReadOnlyCollection<SellerRole> roles)
    {
        if (!CanAct)
        {
            return new HashSet<MerchantPermission>();
        }

        var parId = roles.ToDictionary(r => r.Id);
        var socle = new HashSet<MerchantPermission>();

        foreach (var porte in _sellerRoles)
        {
            if (parId.TryGetValue(porte.RoleId, out var role))
            {
                socle.UnionWith(role.Permissions);
            }
        }

        return socle;
    }

    /// <summary>Les permissions du membre boutique par boutique — socle vendeur COMPRIS.</summary>
    public IReadOnlyDictionary<Guid, IReadOnlySet<MerchantPermission>> PermissionsByStore(
        IReadOnlyCollection<SellerRole> roles)
    {
        if (!CanAct)
        {
            return new Dictionary<Guid, IReadOnlySet<MerchantPermission>>();
        }

        var parId = roles.ToDictionary(r => r.Id);
        var socle = SellerLevelPermissions(roles);
        var parBoutique = new Dictionary<Guid, IReadOnlySet<MerchantPermission>>();

        foreach (var affectation in _storeMemberships.Where(s => s.Status == StoreMembershipStatus.Active))
        {
            var effectives = new HashSet<MerchantPermission>(socle);

            foreach (var roleId in affectation.RoleIds)
            {
                if (parId.TryGetValue(roleId, out var role))
                {
                    effectives.UnionWith(role.Permissions);
                }
            }

            // UNION ET NON REMPLACEMENT : un membre peut être affecté deux fois à
            // la même boutique par deux chemins (invitation puis attribution).
            parBoutique[affectation.StoreId] = parBoutique.TryGetValue(affectation.StoreId, out var deja)
                ? new HashSet<MerchantPermission>(deja.Concat(effectives))
                : effectives;
        }

        return parBoutique;
    }

    /// <summary>Tous les rôles référencés, au niveau vendeur comme au niveau boutique.</summary>
    public IReadOnlySet<SellerRoleId> ReferencedRoleIds
        => _sellerRoles.Select(r => r.RoleId)
            .Concat(_storeMemberships.SelectMany(s => s.RoleIds))
            .ToHashSet();

    // ── Les gardes ──────────────────────────────────────────────────────────

    /// <summary>Quatre conditions, du moins au plus révélateur.</summary>
    private Result EnsureCanAdminister(MemberActor acteur, MerchantPermission requise)
    {
        // 1. LE CLOISONNEMENT PAR VENDEUR. « Introuvable » et non « interdit » :
        // distinguer les deux dirait à qui teste des identifiants lesquels
        // existent, et les identifiants de membres circulent dans l'écran d'équipe.
        if (acteur.SellerId != SellerId)
        {
            return Result.Failure(Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        var habilitation = acteur.Ensure(requise);
        if (habilitation.IsFailure)
        {
            return habilitation;
        }

        // 2. ON NE S'ADMINISTRE PAS SOI-MÊME (§36).
        if (acteur.Id == Id)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.self", "On ne modifie pas ses propres droits."));
        }

        // 3. LE PROPRIÉTAIRE N'EST ADMINISTRABLE QUE PAR UN PROPRIÉTAIRE.
        if (IsOwner && !acteur.IsOwner)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.owner_protected",
                "Seul un propriétaire peut agir sur un propriétaire."));
        }

        return Result.Success();
    }

    /// <summary>LE DERNIER PROPRIÉTAIRE NE PART PAS (§11, §36).</summary>
    private Result EnsureNotLastOwner(bool estDernierProprietaire)
        => IsOwner && estDernierProprietaire
            ? Result.Failure(Error.Conflict(
                "sellers.member.last_owner",
                "Le dernier propriétaire ne peut pas être retiré : transférez la propriété d'abord."))
            : Result.Success();

    private static Result EnsureCanAssign(
        MemberActor acteur, IReadOnlyCollection<SellerRole> roles, Guid? storeId = null)
        => acteur.EnsureCanAssign(roles, storeId);

    internal static string? Trim(string? valeur, int longueurMax)
    {
        var propre = valeur?.Trim();
        return string.IsNullOrEmpty(propre)
            ? null
            : propre.Length > longueurMax ? propre[..longueurMax] : propre;
    }

    private void Touch() => UpdatedOnUtc = DateTime.UtcNow;
}

/// <summary>L'ACTEUR D'UNE MUTATION — UN MEMBRE, SES PERMISSIONS DÉJÀ RÉSOLUES.</summary>
public sealed record MemberActor(
    SellerMemberId Id,
    Guid SellerId,
    Guid UserId,
    bool IsOwner,
    bool CanAct,
    IReadOnlySet<MerchantPermission> Permissions,
    IReadOnlySet<MerchantPermission> SellerLevelPermissions,
    IReadOnlyDictionary<Guid, IReadOnlySet<MerchantPermission>> PermissionsByStore)
{
    /// <summary>
    /// Un acteur SANS aucune affectation boutique : tout ce qu'il porte vaut
    /// partout.
    /// </summary>
    public MemberActor(
        SellerMemberId Id,
        Guid SellerId,
        Guid UserId,
        bool IsOwner,
        bool CanAct,
        IReadOnlySet<MerchantPermission> Permissions)
        : this(
            Id, SellerId, UserId, IsOwner, CanAct,
            Permissions,
            Permissions,
            new Dictionary<Guid, IReadOnlySet<MerchantPermission>>())
    {
    }

    public bool Has(MerchantPermission permission) => CanAct && Permissions.Contains(permission);

    /// <summary>
    /// Ce compte peut-il faire <paramref name="permission"/> DANS cette boutique ?
    /// </summary>
    public bool HasInStore(Guid storeId, MerchantPermission permission)
    {
        if (!CanAct)
        {
            return false;
        }

        return PermissionsByStore.TryGetValue(storeId, out var dansLaBoutique)
            ? dansLaBoutique.Contains(permission)
            : SellerLevelPermissions.Contains(permission);
    }

    public Result Ensure(MerchantPermission permission)
    {
        if (!CanAct)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.not_active", "Votre accès à ce vendeur n'est pas actif."));
        }

        return Permissions.Contains(permission)
            ? Result.Success()
            : Result.Failure(Error.Forbidden(
                "sellers.member.permission_denied",
                $"Vous n'avez pas l'autorisation « {permission.ToCode()} »."));
    }

    /// <summary>ON N'ATTRIBUE QUE CE QU'ON A, ET JAMAIS UN RÔLE D'AUTRUI.</summary>
    /// <param name="storeId">
    /// La boutique à laquelle les rôles sont rattachés, ou <c> null</c> pour une
    /// attribution AU NIVEAU DU VENDEUR.
    /// </param>
    public Result EnsureCanAssign(IReadOnlyCollection<SellerRole> roles, Guid? storeId = null)
    {
        // UNE BOUTIQUE OÙ L'ACTEUR N'EST PAS AFFECTÉ REND LE SOCLE, PAS L'UNION.
        var referentiel = storeId is { } boutique
            ? PermissionsByStore.TryGetValue(boutique, out var dansLaBoutique)
                ? dansLaBoutique
                : SellerLevelPermissions
            : SellerLevelPermissions;

        foreach (var role in roles)
        {
            // Un rôle personnalisé appartient à UN vendeur.
            if (role.SellerId is { } proprietaire && proprietaire != SellerId)
            {
                return Result.Failure(Error.NotFound("sellers.role.not_found", "Rôle introuvable."));
            }

            // LE RÔLE DE PROPRIÉTAIRE NE S'ATTRIBUE PAS PAR CE CHEMIN. Il se
            // transmet par un transfert de propriété, qui est une opération à part
            // — critique, réservée, et destinée à être auditée.
            if (role.Id == SystemSellerRoles.OwnerId)
            {
                return Result.Failure(Error.Forbidden(
                    "sellers.member.owner_role_locked",
                    "Le rôle de propriétaire ne s'attribue que par un transfert de propriété."));
            }

            var manquante = role.Permissions
                .Where(p => !referentiel.Contains(p))
                .Cast<MerchantPermission?>()
                .FirstOrDefault();

            if (manquante is { } absente)
            {
                return Result.Failure(Error.Forbidden(
                    "sellers.member.cannot_delegate",
                    $"Le rôle « {role.Name} » porte « {absente.ToCode()} », dont vous ne disposez pas."));
            }
        }

        return Result.Success();
    }
}

/// <summary>Résolution d'un membre en acteur.</summary>
public static class MemberAccess
{
    public static MemberActor For(SellerMember membre, IReadOnlyCollection<SellerRole> roles)
        => new(
            membre.Id,
            membre.SellerId,
            membre.UserId,
            membre.IsOwner,
            membre.CanAct,

            // L'UNION RESTE LE `Permissions` DE L'ACTEUR, ET NE CHANGE PAS AU LOT
            // F.
            membre.EffectivePermissions(roles),

            // LE SOCLE EST TRANSPORTÉ, PAS RECALCULÉ PAR INTERSECTION.
            membre.SellerLevelPermissions(roles),
            membre.PermissionsByStore(roles));
}

/// <summary>Accès aux membres. L'interface vit dans le fichier de l'agrégat.</summary>
public interface ISellerMemberRepository
{
    Task<SellerMember?> GetByIdAsync(SellerMemberId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// L'appartenance d'un compte à un vendeur donné — la lecture de
    /// l'autorisation.
    /// </summary>
    Task<SellerMember?> GetMembershipAsync(
        Guid sellerId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>UNE SEULE APPARTENANCE ACTIVE, AUJOURD'HUI.</summary>
    Task<SellerMember?> GetActiveMembershipByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellerMember>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Le décompte qui sert l'invariant du dernier propriétaire.</summary>
    Task<int> CountActiveOwnersAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Combien d'équipes vendeur ce compte a-t-il rejointes, tous vendeurs
    /// confondus — le décompte qui décide si le rôle `Seller` doit lui être retiré.
    /// </summary>
    Task<int> CountActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Combien de membres de ce vendeur portent ce rôle — sert au refus de
    /// suppression d'un rôle encore attribué.
    /// </summary>
    Task<int> CountByRoleAsync(
        Guid sellerId, SellerRoleId roleId, CancellationToken cancellationToken = default);

    Task AddAsync(SellerMember member, CancellationToken cancellationToken = default);
}
