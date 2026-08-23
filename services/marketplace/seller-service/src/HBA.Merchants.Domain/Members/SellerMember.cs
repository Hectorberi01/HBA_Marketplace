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
/// <remarks>
/// AUCUN DE CES QUATRE ÉTATS NE SUPPRIME LA LIGNE.
///
/// C'est le même principe que <c>RestaurantStaff</c> : un départ se désactive.
/// Supprimer effacerait qui a ajusté quel stock, et l'audit du §35 lirait des
/// identifiants sans nom. Seul le RGPD supprime, par la purge existante.
/// </remarks>
public enum MemberStatus
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ÉTAT INATTEIGNABLE, ET IL NE FAUT SURTOUT PAS LE RETIRER (lot 9.2).
    ///
    /// Aucun chemin ne crée un membre en `Invited` : l'invitation est un AGRÉGAT
    /// SÉPARÉ (`SellerInvitation`, avec son propre statut), et la ligne de membre
    /// n'est écrite qu'à l'acceptation — donc directement en `Active`.
    ///
    /// MAIS ELLE VAUT ZÉRO, ET LE STATUT EST STOCKÉ EN ENTIER
    /// (`HasConversion&lt;int&gt;`, MemberConfiguration). La retirer ferait de
    /// `default(MemberStatus)` une valeur qui ne nomme plus rien, et tout membre
    /// construit sans statut explicite porterait un état inexistant — en base,
    /// sous la forme d'un `0` que plus aucune ligne de code ne sait lire.
    ///
    /// C'est l'inverse du geste demandé par le lot : ici, retirer coûte, garder
    /// ne coûte rien.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

/// <summary>
/// Où en est l'application du cadrage par boutique, pour CETTE affectation (§8).
/// </summary>
/// <remarks>
/// `Prepared` VEUT DIRE « LA DONNÉE EXISTE, LA RÈGLE NE S'APPLIQUE PAS ENCORE ».
///
/// L'affectation est écrite, l'écran peut l'afficher, et pourtant les permissions
/// qu'elle porte s'appliquent au vendeur entier — parce qu'aucune commande ni aucun
/// article de stock ne connaît la boutique. Le champ dit cette vérité plutôt que de
/// la taire : le jour du lot G, il passe à `Enforced` boutique par boutique, et ce
/// qui change se lit en base.
/// </remarks>
public enum StoreEnforcement
{
    Prepared = 0,

    /// <summary>
    /// ÉTAT INATTEIGNABLE : la moitié « appliquée » n'a jamais été branchée
    /// (lot 9.2). Toute affectation reste `Prepared` — c'est-à-dire préparée et
    /// jamais opposée. Stocké en ENTIER : ne pas renuméroter.
    /// </summary>
    Enforced = 1
}

/// <summary>Un rôle porté par un membre au niveau du vendeur — une ligne de <c>seller_member_roles</c>.</summary>
public sealed class SellerMemberRole
{
    private SellerMemberRole()
    {
    }

    internal SellerMemberRole(SellerRoleId roleId) => RoleId = roleId;

    public SellerRoleId RoleId { get; private set; }
}

/// <summary>Un rôle porté sur une boutique précise — une ligne de <c>store_membership_roles</c>.</summary>
public sealed class StoreMembershipRole
{
    private StoreMembershipRole()
    {
    }

    internal StoreMembershipRole(SellerRoleId roleId) => RoleId = roleId;

    public SellerRoleId RoleId { get; private set; }
}

/// <summary>
/// Affectation d'un membre à une boutique, avec ses rôles.
/// </summary>
/// <remarks>
/// UNE ENTITÉ À PART, AVEC SON PROPRE IDENTIFIANT — comme le demande le §12.2.
///
/// Elle aurait pu être un type possédé à clé composée <c>(SellerMemberId, StoreId)</c>.
/// L'identifiant propre évite d'imbriquer deux niveaux de possession — ses rôles
/// en forment déjà un — et il donne à l'API des membres quelque chose à désigner
/// dans une URL, ce que le cahier utilise (<c>PUT /members/{id}/stores/{storeId}/roles</c>
/// deviendra une désignation par identifiant le jour où un membre pourra être
/// affecté deux fois à la même boutique sous des rôles différents).
/// </remarks>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN MEMBRE DE L'ÉQUIPE D'UN VENDEUR.
///
/// `UserId` VIENT D'IDENTITY. CE MODULE N'EN CRÉE JAMAIS.
///
/// Identity reste la source de vérité de l'identité ; seller-service devient celle
/// de l'APPARTENANCE. C'est ce que dit l'encadré du §2 en refusant un booléen
/// `isEmployee` sur `User` : le compte doit rester indépendant de son rattachement
/// commercial, sans quoi un employé qui change d'entreprise perd son compte.
///
/// TOUTE MUTATION EXIGE UN ACTEUR, ET IL N'EXISTE AUCUNE SURCHARGE SANS.
///
/// Le compilateur refuse donc l'appel non gardé. C'est le seul mécanisme qui tient
/// dans la durée — un contrôle qu'on doit penser à écrire est un contrôle qu'on
/// oubliera, et les gardes de ce dépôt ont déjà été oubliées cinq fois.
///
/// CE QU'IL NE FAIT PAS, ET QUI SE DÉCIDE AILLEURS.
///
///   • Le rôle `Seller` du jeton — c'est identity, par événement (lot B′). Tant
///     qu'il manque, un membre parfaitement écrit ici est refoulé par
///     `MapSellerGroup` avant tout handler.
///   • Le compte du dernier propriétaire — il faut COMPTER, donc lire le dépôt :
///     l'information arrive par paramètre, et le compilateur force l'appelant à
///     l'avoir cherchée.
///   • Le refus d'attribuer un rôle à vocation boutique chez un vendeur qui en a
///     déjà deux (D27) — il faut compter les boutiques, même raison.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// Porteur du rôle système OWNER — une comparaison d'identifiant, pas une requête.
    /// C'est ce que gagne l'identifiant FIXE des rôles système.
    /// </summary>
    public bool IsOwner => _sellerRoles.Any(r => r.RoleId == SystemSellerRoles.OwnerId);

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>
    /// Le propriétaire, créé par l'inscription du vendeur ou par la reprise.
    /// </summary>
    /// <remarks>
    /// SEULE CRÉATION SANS ACTEUR DE TOUT CE TYPE, comme <c>RestaurantStaff.Founder</c>.
    /// Elle n'est appelable que par le module lui-même : il n'y a personne pour
    /// autoriser la naissance du premier membre.
    /// </remarks>
    internal static SellerMember Owner(Guid sellerId, Guid ownerUserId)
    {
        var membre = new SellerMember(
            SellerMemberId.New(), sellerId, ownerUserId, MemberStatus.Active,
            displayName: null, jobTitle: null, invitedByUserId: null);

        membre._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.OwnerId));
        return membre;
    }

    /// <summary>
    /// Un membre rattaché après acceptation d'une invitation.
    /// </summary>
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
        //
        // Fondre les rôles vendeur et les rôles de boutique dans un même tableau
        // mesurait tout à l'union de l'acteur, ce qui laissait un responsable de la
        // boutique A recruter au niveau VENDEUR avec les droits qu'il ne tient que
        // de A — droits qui entraient alors dans le socle de la recrue, donc dans
        // toutes les boutiques. Voir `MemberActor.EnsureCanAssign`.
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

    /// <summary>
    /// Le membre né d'une invitation acceptée.
    /// </summary>
    /// <remarks>
    /// PAS D'ACTEUR ICI, ET C'EST L'EXCEPTION QUI CONFIRME LA RÈGLE.
    ///
    /// L'autorisation a été donnée à la CRÉATION de l'invitation, par un acteur
    /// qui possédait alors `MEMBER_INVITE` et chacune des permissions déléguées.
    /// L'invitation la porte, figée, avec son échéance et son empreinte : la
    /// rejouer ici reviendrait à demander deux fois la même chose, et à faire
    /// dépendre l'entrée d'un employé de l'état, ce jour-là, de celui qui l'a
    /// recruté la semaine précédente.
    ///
    /// Ce que la couche Application doit vérifier avant d'appeler — parce que le
    /// domaine ne lit pas la base : que l'invitation a bien été acceptée par CE
    /// compte, et que celui qui l'a émise fait toujours partie de l'équipe.
    /// </remarks>
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TRANSFERT DE PROPRIÉTÉ — L'OPÉRATION QUE TROIS GARDES RÉCLAMAIENT SANS
    /// QU'ELLE EXISTE (ISSUE-040).
    ///
    /// `SetSellerRoles` refusait de retirer OWNER en disant « il se transfère » ;
    /// `EnsureCanAssign` refusait de l'attribuer en disant « par un transfert de
    /// propriété » ; `EnsureNotLastOwner` refusait de retirer le dernier en disant
    /// « transférez la propriété d'abord ». Aucune de ces trois phrases ne
    /// désignait quoi que ce soit d'écrit.
    ///
    /// STATIQUE, PARCE QU'ELLE MUTE DEUX MEMBRES À LA FOIS.
    ///
    /// Le rôle quitte l'un ET rejoint l'autre. Une méthode d'instance ne pourrait
    /// faire qu'une moitié, et une moitié appliquée seule laisse soit un dossier
    /// SANS propriétaire, soit deux — l'invariant que tout ce fichier protège.
    /// L'appelant reste responsable de la transaction : `Seller.UserId` doit bouger
    /// dans le même `SaveChanges`.
    ///
    /// ON NE TRANSFÈRE QUE SA PROPRE PROPRIÉTÉ.
    ///
    /// L'acteur DOIT être le cédant. Sans cette garde, `OWNERSHIP_TRANSFER`
    /// permettrait à un propriétaire d'en dépouiller un autre — et comme le rôle ne
    /// s'attribue par aucun autre chemin, la victime ne pourrait jamais le
    /// reprendre. C'est l'escalade la plus courte que cette opération pouvait
    /// ouvrir, et c'est la seule qu'elle referme d'elle-même.
    ///
    /// CE QUE CETTE MÉTHODE NE COUVRE PAS.
    ///
    /// Elle suppose le cédant JOIGNABLE. Un dossier dont le propriétaire a
    /// réellement disparu — compte supprimé, personne partie sans transmettre —
    /// reste bloqué : personne d'autre ne porte `OWNERSHIP_TRANSFER`, et aucun
    /// chemin d'administration plateforme ne compose l'équipe d'un commerçant. Le
    /// rattrapage de ce cas-là demande une décision produit, pas une garde de plus.
    /// ═════════════════════════════════════════════════════════════════════════
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
        //
        // Transférer vers un membre suspendu ou révoqué produirait un dossier dont
        // le propriétaire ne peut pas agir — donc, `OWNERSHIP_TRANSFER` étant
        // réservée au propriétaire, un dossier que plus personne ne peut débloquer.
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
        //
        // Un membre sans aucun rôle n'a plus AUCUNE permission : l'ancien
        // propriétaire perdrait jusqu'au droit de voir son équipe, dans le geste
        // même où il transmet. On lui laisse `SELLER_ADMIN`, qui porte tout sauf
        // les six permissions réservées au propriétaire — c'est-à-dire exactement
        // ce qu'il vient de céder, et rien de plus.
        if (cedant._sellerRoles.Count == 0)
        {
            cedant._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.SellerAdminId));
        }

        beneficiaire._sellerRoles.Add(new SellerMemberRole(SystemSellerRoles.OwnerId));
        beneficiaire.Touch();

        // LEVÉ SUR LE BÉNÉFICIAIRE, PAS SUR LE CÉDANT.
        //
        // `ModuleDbContext` dépêche les événements de tous les agrégats suivis :
        // le lever deux fois enverrait deux notifications pour un seul geste. Il
        // porte les deux comptes, donc le consommateur n'a rien à relire.
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
        //
        // Retirer OWNER se fait par un transfert de propriété, qui désigne le
        // successeur dans le même geste. Sans cette garde, une modification de
        // rôles laisserait un dossier sans propriétaire — et la seule route qui
        // permet d'en désigner un exige d'en être un.
        if (IsOwner && !roles.Any(r => r.Id == SystemSellerRoles.OwnerId))
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.owner_role_locked",
                "Le rôle de propriétaire se transfère, il ne se retire pas."));
        }

        // OWNER EST RETIRÉ DE LA DÉLÉGATION QUAND LA CIBLE LE PORTE DÉJÀ.
        //
        // Sans cela, les deux gardes s'annulent et les rôles du propriétaire sont
        // FIGÉS À JAMAIS : celle du dessus exige OWNER dans la liste, `EnsureCanAssign`
        // le refuse — aucune liste ne passe, et le message parle d'un transfert de
        // propriété que l'appelant n'a pas demandé.
        //
        // Le retirer ici ne desserre rien : la garde du dessus a déjà établi que la
        // cible EST propriétaire, donc qu'aucun droit n'est gagné. Sur un membre
        // ordinaire, `IsOwner` est faux, OWNER reste dans la liste, et
        // `EnsureCanAssign` le refuse comme avant.
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
        //
        // Un responsable de la boutique A qui affecte quelqu'un à la boutique B ne
        // peut lui donner que ce qu'il a LUI-MÊME dans B — c'est-à-dire son socle
        // vendeur, puisqu'il n'y est pas affecté. Comparer à l'union lui laisserait
        // transmettre à B les droits qu'il ne tient que de A.
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
        //
        // La révocation est une décision administrative ; la rouvrir d'un clic la
        // viderait de son sens. Réintégrer quelqu'un se fait par une nouvelle
        // invitation, qui laisse sa propre trace et repasse par son consentement.
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
    /// <see cref="SellerMemberRevokedDomainEvent"/>, qui explique pourquoi un
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
        //
        // Un membre suspendu ou révoqué pouvait donc encore éditer sa fiche — et un
        // acteur d'un AUTRE vendeur dont l'identifiant de membre coïnciderait aussi.
        // Sans route exposée, ce n'était pas exploitable ; la première route qui
        // l'exposera héritera du trou si on ne le ferme pas maintenant.
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

    /// <summary>
    /// EN PHASE 1, LES RÔLES DE BOUTIQUE COMPTENT AU NIVEAU DU VENDEUR.
    /// </summary>
    /// <remarks>
    /// C'est la ligne la plus lourde de conséquences du module, et elle est écrite
    /// ici plutôt que déduite ailleurs. Un rôle attaché à une affectation boutique
    /// entre dans l'ensemble effectif SANS filtrage par boutique, parce qu'aucun
    /// des trois services concernés ne sait à quelle boutique appartient une
    /// commande ou un article de stock. L'affectation porte
    /// <see cref="StoreEnforcement.Prepared"/> pour dire exactement cela.
    ///
    /// Au lot G, cette méthode prendra un <c>storeId</c> et ne retiendra que les
    /// affectations `Enforced` correspondantes. D'ici là, le garde-fou est
    /// ailleurs : on refuse d'attribuer un rôle à vocation boutique dès que le
    /// vendeur en a plus d'une (D27).
    /// </remarks>
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
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST LE SOCLE : CE QUI VAUT PARTOUT, QUELLE QUE SOIT LA BOUTIQUE.
    ///
    /// Un comptable rattaché au vendeur lit les finances de l'entreprise, pas
    /// celles d'une boutique — la notion n'aurait pas de sens. Ces permissions-là
    /// entrent donc dans TOUS les périmètres, et ce sont les seules.
    ///
    /// La distinction avec <see cref="EffectivePermissions"/> est tout le lot F :
    /// la première rend l'union de tout, y compris ce qui vient des boutiques ; la
    /// seconde ne rend que le socle. Les confondre annulerait le cadrage.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

    /// <summary>
    /// Les permissions du membre boutique par boutique — socle vendeur COMPRIS.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CHAQUE ENTRÉE CONTIENT LE SOCLE, ET CE N'EST PAS UNE DUPLICATION INUTILE.
    ///
    /// L'appelant pose une question fermée — « ce compte peut-il faire X dans la
    /// boutique B » — et doit pouvoir y répondre par une seule recherche. Lui
    /// rendre les seuls rôles de la boutique l'obligerait à réunir deux ensembles
    /// lui-même, à chaque garde, dans cinq services : quatre occasions d'oublier
    /// le socle, et le comptable perdrait ses finances dès qu'une boutique serait
    /// nommée dans l'URL.
    ///
    /// Le coût est quelques dizaines de valeurs recopiées par boutique, dans une
    /// réponse déjà mise en cache deux minutes.
    ///
    /// SEULES LES AFFECTATIONS ACTIVES COMPTENT.
    ///
    /// Une affectation suspendue n'est pas une affectation à droits réduits : elle
    /// ne donne plus rien de la boutique. Celle-ci disparaît simplement du
    /// dictionnaire, et `HasInStore` retombe alors sur le SOCLE — le membre garde ce
    /// qu'il tient du niveau vendeur, et rien de la boutique dont on l'a retiré.
    /// </remarks>
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
            // la même boutique par deux chemins (invitation puis attribution). Écraser
            // retiendrait le dernier lu, c'est-à-dire un ordre d'énumération.
            parBoutique[affectation.StoreId] = parBoutique.TryGetValue(affectation.StoreId, out var deja)
                ? new HashSet<MerchantPermission>(deja.Concat(effectives))
                : effectives;
        }

        return parBoutique;
    }

    /// <summary>
    /// Tous les rôles référencés, au niveau vendeur comme au niveau boutique.
    /// C'est ce que la couche Application charge avant de résoudre un acteur.
    /// </summary>
    public IReadOnlySet<SellerRoleId> ReferencedRoleIds
        => _sellerRoles.Select(r => r.RoleId)
            .Concat(_storeMemberships.SelectMany(s => s.RoleIds))
            .ToHashSet();

    // ── Les gardes ──────────────────────────────────────────────────────────

    /// <summary>Quatre conditions, du moins au plus révélateur.</summary>
    private Result EnsureCanAdminister(MemberActor acteur, MerchantPermission requise)
    {
        // 1. LE CLOISONNEMENT PAR VENDEUR. « Introuvable » et non « interdit » :
        //    distinguer les deux dirait à qui teste des identifiants lesquels
        //    existent, et les identifiants de membres circulent dans l'écran
        //    d'équipe.
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
        //
        // Sans cela, un SELLER_ADMIN — qui porte MEMBER_REVOKE — révoquerait le
        // propriétaire du dossier et resterait seul aux commandes. C'est
        // l'escalade la plus courte du module.
        if (IsOwner && !acteur.IsOwner)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.member.owner_protected",
                "Seul un propriétaire peut agir sur un propriétaire."));
        }

        return Result.Success();
    }

    /// <summary>
    /// LE DERNIER PROPRIÉTAIRE NE PART PAS (§11, §36).
    ///
    /// Le décompte vient de l'appelant : le domaine ne lit pas la base. Le
    /// paramètre est obligatoire, donc aucun chemin ne peut l'ignorer par
    /// distraction — c'est le même principe que l'acteur obligatoire.
    /// </summary>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ACTEUR D'UNE MUTATION — UN MEMBRE, SES PERMISSIONS DÉJÀ RÉSOLUES.
///
/// POURQUOI CE TYPE EXISTE PLUTÔT QUE DE PASSER UN `SellerMember`.
///
/// Les permissions d'un membre vivent dans des rôles, qui sont d'AUTRES agrégats.
/// Un `SellerMember` seul ne sait pas ce qu'il a le droit de faire : il ne connaît
/// que des identifiants de rôles. Passer l'agrégat brut obligerait chaque garde à
/// aller lire le dépôt — ce qu'un agrégat ne fait pas.
///
/// Ce type est donc la forme sous laquelle un acteur entre dans le domaine :
/// résolu une fois, par <see cref="MemberAccess.For"/>, à un seul endroit et testé
/// comme tel.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE N'EST PAS UNE COMMODITÉ DE TEST, C'EST UN ÉTAT RÉEL ET FRÉQUENT.
    ///
    /// Le propriétaire est exactement cela : ses rôles sont au niveau du vendeur,
    /// il n'a aucune affectation, et le cadrage n'a rien à retrancher — le socle
    /// EST l'ensemble complet. Un comptable rattaché au vendeur aussi.
    ///
    /// ET C'EST POUR CELA QU'ELLE EST DANGEREUSE SI ON S'EN SERT AILLEURS.
    ///
    /// L'employer pour un membre QUI A des affectations poserait
    /// `SellerLevelPermissions == Permissions`, c'est-à-dire « tout ce qu'il porte
    /// vaut dans toute boutique » — précisément le trou que le lot F ferme, rouvert
    /// par un raccourci de construction. `MemberAccess.For` est le seul chemin
    /// correct pour un membre lu en base ; celui-ci sert à fabriquer un acteur dont
    /// on SAIT qu'il n'a pas de boutique.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <remarks>
    /// LES PARAMÈTRES PORTENT LA CASSE DES MEMBRES, PAS CELLE D'UN CONSTRUCTEUR.
    ///
    /// `IsOwner` et non `isOwner` : les appelants existants passent des arguments
    /// NOMMÉS — `IsOwner: false, CanAct: true` — parce que six positions dont deux
    /// booléens côte à côte s'inversent silencieusement. Renommer en camelCase
    /// casserait chacun d'eux à la compilation, pour une convention que le record
    /// lui-même ne suit pas : ses paramètres positionnels s'appellent déjà ainsi.
    /// </remarks>
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
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// STRICTEMENT PLUS ÉTROIT QUE <see cref="Has"/>, ET C'EST LE LOT F.
    ///
    /// `Has` répond sur l'UNION de tout ce que le membre porte, boutiques
    /// confondues : un responsable de la boutique A y voit donc les commandes de
    /// la boutique B. Cette méthode-ci ne retient que le socle vendeur et les
    /// rôles de LA boutique visée.
    ///
    /// LE PROPRIÉTAIRE PASSE PAR `Has`, ET C'EST VOULU.
    ///
    /// Il n'a aucune affectation boutique — il n'en a pas besoin, ses rôles sont
    /// au niveau vendeur, donc dans le socle, donc dans chaque périmètre. Aucun
    /// cas particulier à écrire : c'est la structure qui le porte.
    ///
    /// UNE BOUTIQUE INCONNUE DU DICTIONNAIRE RETOMBE SUR LE SOCLE.
    ///
    /// Pas sur `Permissions` : retomber sur l'union serait exactement le trou qu'on
    /// ferme — un STORE_ADMIN de A recevrait ses droits sur B au motif qu'il n'y est
    /// pas affecté. Pas « rien » non plus : le comptable rattaché au VENDEUR lit les
    /// finances partout, y compris dans une boutique qui ne le connaît pas. Le socle
    /// est la seule réponse juste, et c'est `SellerLevelPermissions` qui le porte.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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

    /// <summary>
    /// ON N'ATTRIBUE QUE CE QU'ON A, ET JAMAIS UN RÔLE D'AUTRUI.
    /// </summary>
    /// <remarks>
    /// Elle vit sur l'ACTEUR et non sur le membre, parce que les deux chemins qui
    /// attribuent des rôles n'ont pas de membre sous la main : l'invitation en
    /// attribue avant que le membre n'existe. La règle serait sinon écrite deux
    /// fois — et c'est celle qui empêche un gérant de recruter quelqu'un de plus
    /// puissant que lui.
    /// </remarks>
    /// <param name="storeId">
    /// La boutique à laquelle les rôles sont rattachés, ou <c>null</c> pour une
    /// attribution AU NIVEAU DU VENDEUR.
    /// </param>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ON COMPARE AU PÉRIMÈTRE VISÉ, PLUS À L'UNION — ET C'EST UN CORRECTIF.
    ///
    /// La version d'origine comparait les permissions demandées à
    /// <see cref="Permissions"/>, l'union tous périmètres confondus. Depuis le lot F,
    /// cette union est plus large que ce que l'acteur peut réellement faire quelque
    /// part, et l'écart est exploitable :
    ///
    ///   M est STORE_ADMIN sur la seule boutique A. Son union contient donc
    ///   STORE_UPDATE. Il invite N en lui donnant STORE_ADMIN AU NIVEAU DU VENDEUR.
    ///   La comparaison à l'union passe. STORE_UPDATE entre alors dans le SOCLE de
    ///   N — donc dans toutes les boutiques, B comprise. M vient de fabriquer un
    ///   compte qui administre une boutique sur laquelle il n'a lui-même aucun droit.
    ///
    /// Le blanchiment tient à ce que l'union de l'acteur et le socle de la cible
    /// sont deux choses différentes que l'ancienne règle traitait comme une seule.
    ///
    /// D'OÙ LES DEUX RÉFÉRENTIELS.
    ///
    ///   attribution au niveau vendeur  → <see cref="SellerLevelPermissions"/>
    ///   attribution sur la boutique B  → <see cref="PermissionsByStore"/>[B]
    ///
    /// Le propriétaire n'a aucune affectation : son socle EST son union, et rien ne
    /// se ferme pour lui. Un membre affecté à B qui recrute POUR B compare à ce
    /// qu'il a dans B — ce qui est exactement son autorité.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Result EnsureCanAssign(IReadOnlyCollection<SellerRole> roles, Guid? storeId = null)
    {
        // UNE BOUTIQUE OÙ L'ACTEUR N'EST PAS AFFECTÉ REND LE SOCLE, PAS L'UNION.
        //
        // Même règle que `HasInStore`, et pour la même raison : retomber sur l'union
        // rendrait la garde inopérante précisément là où elle protège — un membre
        // recrutant pour une boutique qui n'est pas la sienne.
        var referentiel = storeId is { } boutique
            ? PermissionsByStore.TryGetValue(boutique, out var dansLaBoutique)
                ? dansLaBoutique
                : SellerLevelPermissions
            : SellerLevelPermissions;

        foreach (var role in roles)
        {
            // Un rôle personnalisé appartient à UN vendeur. Attribuer celui d'un
            // concurrent donnerait des permissions qu'on n'a pas eu à justifier.
            if (role.SellerId is { } proprietaire && proprietaire != SellerId)
            {
                return Result.Failure(Error.NotFound("sellers.role.not_found", "Rôle introuvable."));
            }

            // LE RÔLE DE PROPRIÉTAIRE NE S'ATTRIBUE PAS PAR CE CHEMIN.
            // Il se transmet par un transfert de propriété, qui est une opération
            // à part — critique, réservée, et destinée à être auditée.
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

/// <summary>Résolution d'un membre en acteur. Le seul endroit qui compose les deux agrégats.</summary>
public static class MemberAccess
{
    public static MemberActor For(SellerMember membre, IReadOnlyCollection<SellerRole> roles)
        => new(
            membre.Id,
            membre.SellerId,
            membre.UserId,
            membre.IsOwner,
            membre.CanAct,

            // L'UNION RESTE LE `Permissions` DE L'ACTEUR, ET NE CHANGE PAS AU LOT F.
            //
            // La resserrer sur le socle aurait cadré toutes les routes d'un coup —
            // y compris les dizaines qui ne connaissent AUCUNE boutique et ne
            // peuvent donc pas en nommer une. Un responsable de boutique aurait
            // perdu, sans transition, l'accès à tout ce que le service n'a pas
            // encore appris à situer. Le cadrage s'ajoute route par route via
            // `HasInStore` ; il ne se substitue pas d'un bloc.
            membre.EffectivePermissions(roles),

            // LE SOCLE EST TRANSPORTÉ, PAS RECALCULÉ PAR INTERSECTION.
            //
            // La tentation est forte : chaque entrée de `PermissionsByStore` contenant
            // le socle, leur intersection paraît le redonner. Elle ne le redonne pas.
            // Un membre STORE_ADMIN sur A ET sur B a `STORE_UPDATE` dans les deux
            // entrées : l'intersection la contient, et `HasInStore(C, STORE_UPDATE)`
            // rendrait vrai sur une boutique C où il n'est pas affecté. C'est
            // exactement le trou que ce lot ferme, reconstitué par une astuce.
            membre.SellerLevelPermissions(roles),
            membre.PermissionsByStore(roles));
}

/// <summary>Accès aux membres. L'interface vit dans le fichier de l'agrégat.</summary>
public interface ISellerMemberRepository
{
    Task<SellerMember?> GetByIdAsync(SellerMemberId id, CancellationToken cancellationToken = default);

    /// <summary>L'appartenance d'un compte à un vendeur donné — la lecture de l'autorisation.</summary>
    Task<SellerMember?> GetMembershipAsync(
        Guid sellerId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// UNE SEULE APPARTENANCE ACTIVE, AUJOURD'HUI.
    ///
    /// Le §55 vise plusieurs organisations par compte, et cette signature ne le
    /// permet pas — c'est délibéré, et c'est le point à changer le jour de
    /// l'option B. Le contrat gRPC, lui, prend déjà `(userId, sellerId)` pour ne
    /// pas avoir à bouger ce jour-là.
    /// </summary>
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
    /// <remarks>
    /// À APPELER AVANT LA MUTATION. L'appartenance qu'on s'apprête à révoquer
    /// est encore comptée : « il en a une autre » se lit donc <c>&gt; 1</c>. Après
    /// coup, l'événement de domaine serait déjà parti.
    /// </remarks>
    Task<int> CountActiveMembershipsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Combien de membres de ce vendeur portent ce rôle — sert au refus de
    /// suppression d'un rôle encore attribué.
    /// </summary>
    Task<int> CountByRoleAsync(
        Guid sellerId, SellerRoleId roleId, CancellationToken cancellationToken = default);

    Task AddAsync(SellerMember member, CancellationToken cancellationToken = default);
}
