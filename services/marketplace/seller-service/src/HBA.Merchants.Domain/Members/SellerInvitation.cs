using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Members;

/// <summary>Identité forte d'une invitation.</summary>
public readonly record struct SellerInvitationId(Guid Value)
{
    public static SellerInvitationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statuts du §8 du cahier.</summary>
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>Un rôle promis par une invitation, éventuellement sur une boutique.</summary>
public sealed class InvitationAssignment
{
    private InvitationAssignment()
    {
    }

    internal InvitationAssignment(Guid? storeId, SellerRoleId roleId)
    {
        StoreId = storeId;
        RoleId = roleId;
    }

    public int Id { get; private set; }

    /// <summary>Nul pour un rôle de niveau vendeur.</summary>
    public Guid? StoreId { get; private set; }

    public SellerRoleId RoleId { get; private set; }
}

/// <summary>UNE INVITATION — CE QUI EXISTE AVANT QU'UN MEMBRE N'EXISTE.</summary>
public sealed class SellerInvitation : AggregateRoot<SellerInvitationId>
{
    /// <summary>Durée de validité par défaut — sept jours, comme l'exemple du §7.</summary>
    public static readonly TimeSpan DureeParDefaut = TimeSpan.FromDays(7);

    private readonly List<InvitationAssignment> _assignments = [];

    private SellerInvitation()
    {
    }

    private SellerInvitation(
        SellerInvitationId id, Guid sellerId, string email, string? displayName, string? jobTitle,
        string tokenHash, DateTime expiresOnUtc, Guid invitedByUserId)
        : base(id)
    {
        SellerId = sellerId;
        Email = email;
        DisplayName = displayName;
        JobTitle = jobTitle;
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
        InvitedByUserId = invitedByUserId;
        Status = InvitationStatus.Pending;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid SellerId { get; private set; }

    /// <summary>Normalisée en minuscules : la comparaison à l'acceptation en dépend.</summary>
    public string Email { get; private set; } = default!;

    public string? DisplayName { get; private set; }

    public string? JobTitle { get; private set; }

    public InvitationStatus Status { get; private set; }

    /// <summary>L'EMPREINTE, JAMAIS LE TOKEN.</summary>
    public string TokenHash { get; private set; } = default!;

    public DateTime ExpiresOnUtc { get; private set; }

    public Guid InvitedByUserId { get; private set; }

    public Guid? AcceptedByUserId { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? ResolvedOnUtc { get; private set; }

    public IReadOnlyCollection<InvitationAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Les rôles promis au niveau du vendeur.</summary>
    public IReadOnlyCollection<SellerRoleId> SellerRoleIds
        => [.. _assignments.Where(a => a.StoreId is null).Select(a => a.RoleId)];

    /// <summary>Les rôles promis, groupés par boutique.</summary>
    public IReadOnlyCollection<(Guid StoreId, IReadOnlyCollection<SellerRoleId> RoleIds)> StoreAssignments
        => _assignments
            .Where(a => a.StoreId is not null)
            .GroupBy(a => a.StoreId!.Value)
            .Select(g => (g.Key, (IReadOnlyCollection<SellerRoleId>)g.Select(a => a.RoleId).ToArray()))
            .ToArray();

    /// <summary>Tous les rôles référencés — ce que la couche Application doit charger.</summary>
    public IReadOnlySet<SellerRoleId> ReferencedRoleIds
        => _assignments.Select(a => a.RoleId).ToHashSet();

    // ── Création ────────────────────────────────────────────────────────────

    public static Result<SellerInvitation> Create(
        MemberActor acteur,
        string email,
        string? displayName,
        string? jobTitle,
        IReadOnlyCollection<SellerRole> rolesVendeur,
        IReadOnlyCollection<(Guid StoreId, IReadOnlyCollection<SellerRole> Roles)> affectations,
        string tokenHash,
        DateTime expiresOnUtc)
    {
        var adresse = Normaliser(email);
        if (adresse is null)
        {
            return Error.Validation("sellers.invitation.email_invalid", "Adresse e-mail invalide.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Error.Validation("sellers.invitation.token_required", "Empreinte de jeton manquante.");
        }

        var habilitation = acteur.Ensure(MerchantPermission.MemberInvite);
        if (habilitation.IsFailure)
        {
            return habilitation.Error;
        }

        var tousRoles = rolesVendeur.Concat(affectations.SelectMany(a => a.Roles)).ToArray();

        // ON INVITE SANS AUCUN RÔLE PLUTÔT QUE DE LAISSER LE CHOIX IMPLICITE.
        if (tousRoles.Length == 0)
        {
            return Error.Validation(
                "sellers.invitation.roles_required", "Une invitation doit porter au moins un rôle.");
        }

        // DEUX PÉRIMÈTRES, DEUX CONTRÔLES — MÊME CORRECTIF QUE `SellerMember.Join`.
        var delegation = acteur.EnsureCanAssign(rolesVendeur);
        if (delegation.IsSuccess)
        {
            foreach (var affectation in affectations)
            {
                delegation = acteur.EnsureCanAssign(affectation.Roles, affectation.StoreId);
                if (delegation.IsFailure)
                {
                    break;
                }
            }
        }

        if (delegation.IsFailure)
        {
            return delegation.Error;
        }

        var invitation = new SellerInvitation(
            SellerInvitationId.New(), acteur.SellerId, adresse,
            SellerMember.Trim(displayName, 150), SellerMember.Trim(jobTitle, 120),
            tokenHash, expiresOnUtc, acteur.UserId);

        foreach (var role in rolesVendeur.DistinctBy(r => r.Id))
        {
            invitation._assignments.Add(new InvitationAssignment(null, role.Id));
        }

        foreach (var (storeId, roles) in affectations)
        {
            foreach (var role in roles.DistinctBy(r => r.Id))
            {
                invitation._assignments.Add(new InvitationAssignment(storeId, role.Id));
            }
        }

        // AUCUN ÉVÉNEMENT DE DOMAINE ICI. C'est le handler de commande qui publie,
        // parce que l'événement doit porter le JETON et que cet agrégat ne connaît
        // que son empreinte.
        return invitation;
    }

    // ── Cycle de vie ────────────────────────────────────────────────────────

    /// <summary>Accepte l'invitation au nom d'un compte.</summary>
    /// <param name="emailDuCompte">
    /// Lu chez identity, jamais fourni par l'appelant : c'est tout l'intérêt du
    /// contrôle.
    /// </param>
    public Result Accept(Guid userId, string emailDuCompte, DateTime maintenantUtc)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "sellers.invitation.user_required", "Compte acceptant manquant."));
        }

        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                Status switch
                {
                    InvitationStatus.Accepted => "sellers.invitation.already_accepted",
                    InvitationStatus.Revoked => "sellers.invitation.revoked",
                    InvitationStatus.Declined => "sellers.invitation.declined",
                    _ => "sellers.invitation.expired"
                },
                "Cette invitation n'est plus utilisable."));
        }

        if (maintenantUtc > ExpiresOnUtc)
        {
            // ON POSE LE STATUT AU PASSAGE, ET C'EST VOULU.
            Status = InvitationStatus.Expired;
            ResolvedOnUtc = maintenantUtc;

            return Result.Failure(Error.Conflict(
                "sellers.invitation.expired", "Cette invitation a expiré."));
        }

        if (!string.Equals(Normaliser(emailDuCompte), Email, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Forbidden(
                "sellers.invitation.email_mismatch",
                "Cette invitation a été émise pour une autre adresse."));
        }

        Status = InvitationStatus.Accepted;
        AcceptedByUserId = userId;
        ResolvedOnUtc = maintenantUtc;

        return Result.Success();
    }

    public Result Decline(Guid userId, string emailDuCompte)
    {
        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "sellers.invitation.not_pending", "Cette invitation n'est plus en attente."));
        }

        if (!string.Equals(Normaliser(emailDuCompte), Email, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Forbidden(
                "sellers.invitation.email_mismatch",
                "Cette invitation a été émise pour une autre adresse."));
        }

        Status = InvitationStatus.Declined;
        AcceptedByUserId = userId;
        ResolvedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    public Result Revoke(MemberActor acteur)
    {
        var garde = EnsureGouvernable(acteur, MerchantPermission.MemberInvite);
        if (garde.IsFailure)
        {
            return garde;
        }

        if (Status == InvitationStatus.Revoked)
        {
            return Result.Success();
        }

        if (Status != InvitationStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "sellers.invitation.not_pending", "Seule une invitation en attente se révoque."));
        }

        Status = InvitationStatus.Revoked;
        ResolvedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>Renvoie l'invitation : nouvelle empreinte, nouvelle échéance.</summary>
    /// <param name="rolesPromis">
    /// Les rôles que cette invitation attribuera, indexés par identifiant.
    /// </param>
    public Result Refresh(
        MemberActor acteur,
        string tokenHash,
        DateTime expiresOnUtc,
        IReadOnlyDictionary<SellerRoleId, SellerRole> rolesPromis)
    {
        var garde = EnsureGouvernable(acteur, MerchantPermission.MemberInvite);
        if (garde.IsFailure)
        {
            return garde;
        }

        // LA DÉLÉGATION SE REJOUE À LA RELANCE, ET SON ABSENCE ÉTAIT UNE FAILLE.
        foreach (var groupe in _assignments.GroupBy(a => a.StoreId))
        {
            var roles = groupe
                .Where(a => rolesPromis.ContainsKey(a.RoleId))
                .Select(a => rolesPromis[a.RoleId])
                .ToArray();

            // UN RÔLE DISPARU DEPUIS L'ÉMISSION N'EST PAS IGNORÉ EN SILENCE.
            if (roles.Length != groupe.Count())
            {
                return Result.Failure(Error.Conflict(
                    "sellers.invitation.role_missing",
                    "Un rôle prévu par cette invitation n'existe plus. Émettez-en une nouvelle."));
            }

            var delegation = acteur.EnsureCanAssign(roles, groupe.Key);
            if (delegation.IsFailure)
            {
                return delegation;
            }
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return Result.Failure(Error.Validation(
                "sellers.invitation.token_required", "Empreinte de jeton manquante."));
        }

        // Une invitation expirée se relance ; une invitation acceptée, révoquée ou
        // refusée est close — la relancer effacerait la décision qui l'a close.
        if (Status is not (InvitationStatus.Pending or InvitationStatus.Expired))
        {
            return Result.Failure(Error.Conflict(
                "sellers.invitation.not_pending", "Cette invitation est close."));
        }

        Status = InvitationStatus.Pending;
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
        ResolvedOnUtc = null;

        // Même remarque qu'à la création : le renvoi est annoncé par le handler,
        // qui détient le jeton neuf.
        return Result.Success();
    }

    private Result EnsureGouvernable(MemberActor acteur, MerchantPermission requise)
    {
        // « Introuvable » et non « interdit » : les identifiants d'invitation
        // circulent, et distinguer les deux dirait lesquels existent ailleurs.
        if (acteur.SellerId != SellerId)
        {
            return Result.Failure(Error.NotFound(
                "sellers.invitation.not_found", "Invitation introuvable."));
        }

        return acteur.Ensure(requise);
    }

    private static string? Normaliser(string? email)
    {
        var propre = email?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(propre) || propre.Length > 200)
        {
            return null;
        }

        // Contrôle volontairement minimal : la validation de forme appartient au
        // validateur de la commande, et identity reste seul juge d'une adresse.
        var arobase = propre.IndexOf('@');
        return arobase > 0 && arobase < propre.Length - 1 ? propre : null;
    }
}

/// <summary>Accès aux invitations.</summary>
public interface ISellerInvitationRepository
{
    Task<SellerInvitation?> GetByIdAsync(
        SellerInvitationId id, CancellationToken cancellationToken = default);

    /// <summary>LA RECHERCHE SE FAIT PAR EMPREINTE, JAMAIS PAR JETON.</summary>
    Task<SellerInvitation?> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellerInvitation>> ListBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Une seule invitation en attente par adresse et par vendeur.</summary>
    Task<SellerInvitation?> GetPendingAsync(
        Guid sellerId, string email, CancellationToken cancellationToken = default);

    Task AddAsync(SellerInvitation invitation, CancellationToken cancellationToken = default);
}
