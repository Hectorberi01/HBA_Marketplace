using HBA.Identity.Contracts;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Contracts.IntegrationEvents;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Shared.IntegrationEvents;

namespace HBA.Merchants.Application.Members;

/// <summary>Une affectation demandée : une boutique, des rôles.</summary>
public sealed record StoreAssignmentInput(Guid StoreId, IReadOnlyList<Guid> RoleIds);

/// <summary>Ce que rend une invitation émise ou renvoyée.</summary>
public sealed record InvitationIssued(Guid InvitationId, string Email, string Token, DateTime ExpiresOnUtc);

public sealed record InviteMemberCommand(
    Guid SellerId,
    Guid ActorUserId,
    string Email,
    string? DisplayName,
    string? JobTitle,
    IReadOnlyList<Guid> SellerRoleIds,
    IReadOnlyList<StoreAssignmentInput> Stores) : ICommand<InvitationIssued>;

public sealed record ResendInvitationCommand(
    Guid SellerId, Guid ActorUserId, Guid InvitationId) : ICommand<InvitationIssued>;

public sealed record RevokeInvitationCommand(
    Guid SellerId, Guid ActorUserId, Guid InvitationId) : ICommand;

/// <summary>L'acceptation.</summary>
public sealed record AcceptInvitationCommand(string Token, Guid UserId) : ICommand<Guid>;

public sealed record SetMemberRolesCommand(
    Guid SellerId, Guid ActorUserId, Guid MemberId, IReadOnlyList<Guid> RoleIds) : ICommand;

public sealed record AssignMemberStoreCommand(
    Guid SellerId, Guid ActorUserId, Guid MemberId, Guid StoreId, IReadOnlyList<Guid> RoleIds) : ICommand;

public sealed record UnassignMemberStoreCommand(
    Guid SellerId, Guid ActorUserId, Guid MemberId, Guid StoreId) : ICommand;

public sealed record SuspendMemberCommand(Guid SellerId, Guid ActorUserId, Guid MemberId) : ICommand;

public sealed record ReactivateMemberCommand(Guid SellerId, Guid ActorUserId, Guid MemberId) : ICommand;

public sealed record RevokeMemberCommand(Guid SellerId, Guid ActorUserId, Guid MemberId) : ICommand;

/// <summary>Le départ volontaire — le seul geste qu'un membre pose sur lui-même.</summary>
public sealed record LeaveSellerCommand(Guid SellerId, Guid ActorUserId) : ICommand;

/// <summary>LES COMMANDES DE L'ÉQUIPE — UN SEUL HANDLER, COMME POUR LES BOUTIQUES.</summary>
internal sealed class MemberCommandHandler :
    ICommandHandler<InviteMemberCommand, InvitationIssued>,
    ICommandHandler<ResendInvitationCommand, InvitationIssued>,
    ICommandHandler<RevokeInvitationCommand>,
    ICommandHandler<AcceptInvitationCommand, Guid>,
    ICommandHandler<SetMemberRolesCommand>,
    ICommandHandler<AssignMemberStoreCommand>,
    ICommandHandler<UnassignMemberStoreCommand>,
    ICommandHandler<SuspendMemberCommand>,
    ICommandHandler<ReactivateMemberCommand>,
    ICommandHandler<RevokeMemberCommand>,
    ICommandHandler<LeaveSellerCommand>
{
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRoleRepository _roles;
    private readonly ISellerInvitationRepository _invitations;
    private readonly ISellerRepository _sellers;
    private readonly IStoreRepository _stores;
    private readonly IIdentityModuleApi _identity;
    private readonly IInvitationTokens _tokens;
    private readonly ISecretProtector _protecteur;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly MemberAccessResolver _acces;
    private readonly ISellerUnitOfWork _unitOfWork;

    public MemberCommandHandler(
        ISellerMemberRepository members,
        ISellerRoleRepository roles,
        ISellerInvitationRepository invitations,
        ISellerRepository sellers,
        IStoreRepository stores,
        IIdentityModuleApi identity,
        IInvitationTokens tokens,
        ISecretProtector protecteur,
        IIntegrationEventPublisher publisher,
        MemberAccessResolver acces,
        ISellerUnitOfWork unitOfWork)
    {
        _members = members;
        _roles = roles;
        _invitations = invitations;
        _sellers = sellers;
        _stores = stores;
        _identity = identity;
        _tokens = tokens;
        _protecteur = protecteur;
        _publisher = publisher;
        _acces = acces;
        _unitOfWork = unitOfWork;
    }

    // Invitations

    public async Task<Result<InvitationIssued>> Handle(
        InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(command.SellerId, command.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<InvitationIssued>(acteur.Error);
        }

        var adresse = command.Email.Trim().ToLowerInvariant();

        // UNE SEULE INVITATION VIVANTE PAR ADRESSE ET PAR VENDEUR.
        if (await _invitations.GetPendingAsync(command.SellerId, adresse, cancellationToken) is not null)
        {
            return Result.Failure<InvitationIssued>(Error.Conflict(
                "sellers.invitation.already_pending",
                "Une invitation est déjà en attente pour cette adresse."));
        }

        var deja = await _identity.GetUserByEmailAsync(adresse, cancellationToken);
        if (deja is not null
            && await _members.GetMembershipAsync(command.SellerId, deja.Id, cancellationToken) is not null)
        {
            return Result.Failure<InvitationIssued>(Error.Conflict(
                "sellers.member.already_exists", "Ce compte fait déjà partie de l'équipe."));
        }

        var resolution = await ResoudreRolesAsync(command.SellerRoleIds, command.Stores, cancellationToken);
        if (resolution.IsFailure)
        {
            return Result.Failure<InvitationIssued>(resolution.Error);
        }

        // `Boutiques` ET NON `Tous` : les rôles vendeur de cette invitation sont un
        // choix explicite du vendeur, ils ne promettent aucun cloisonnement.
        var portee = await EnsurePorteeBoutiqueAsync(
            command.SellerId,
            [.. resolution.Value.Boutiques.SelectMany(b => b.Roles)],
            cancellationToken);

        if (portee.IsFailure)
        {
            return Result.Failure<InvitationIssued>(portee.Error);
        }

        var (token, empreinte) = _tokens.Create();
        var echeance = DateTime.UtcNow.Add(SellerInvitation.DureeParDefaut);

        var invitation = SellerInvitation.Create(
            acteur.Value, adresse, command.DisplayName, command.JobTitle,
            resolution.Value.Vendeur, resolution.Value.Boutiques, empreinte, echeance);

        if (invitation.IsFailure)
        {
            return Result.Failure<InvitationIssued>(invitation.Error);
        }

        await _invitations.AddAsync(invitation.Value, cancellationToken);

        var annonce = await AnnoncerAsync(invitation.Value, token, cancellationToken);
        if (annonce.IsFailure)
        {
            return Result.Failure<InvitationIssued>(annonce.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InvitationIssued(invitation.Value.Id.Value, adresse, token, echeance);
    }

    public async Task<Result<InvitationIssued>> Handle(
        ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(command.SellerId, command.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<InvitationIssued>(acteur.Error);
        }

        var invitation = await _invitations.GetByIdAsync(
            new SellerInvitationId(command.InvitationId), cancellationToken);

        if (invitation is null)
        {
            return Result.Failure<InvitationIssued>(
                Error.NotFound("sellers.invitation.not_found", "Invitation introuvable."));
        }

        // LES RÔLES PROMIS SONT RÉSOLUS POUR QUE LA DÉLÉGATION SOIT REJOUÉE.
        var promis = (await _roles.ListByIdsAsync(
                [.. invitation.Assignments.Select(a => a.RoleId).Distinct()], cancellationToken))
            .ToDictionary(r => r.Id);

        var (token, empreinte) = _tokens.Create();
        var echeance = DateTime.UtcNow.Add(SellerInvitation.DureeParDefaut);

        var relance = invitation.Refresh(acteur.Value, empreinte, echeance, promis);
        if (relance.IsFailure)
        {
            return Result.Failure<InvitationIssued>(relance.Error);
        }

        var annonce = await AnnoncerAsync(invitation, token, cancellationToken);
        if (annonce.IsFailure)
        {
            return Result.Failure<InvitationIssued>(annonce.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InvitationIssued(invitation.Id.Value, invitation.Email, token, echeance);
    }

    /// <summary>L'ANNONCE QUI PORTE LE JETON — LE SEUL ÉVÉNEMENT PUBLIÉ HORS DU DOMAINE.</summary>
    private async Task<Result> AnnoncerAsync(
        SellerInvitation invitation, string token, CancellationToken cancellationToken)
    {
        var vendeur = await _sellers.GetByIdAsync(new SellerId(invitation.SellerId), cancellationToken);
        if (vendeur is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", "Vendeur introuvable."));
        }

        await _publisher.PublishAsync(
            new SellerMemberInvitedIntegrationEvent
            {
                SellerId = invitation.SellerId,
                InvitationId = invitation.Id.Value,
                Email = invitation.Email,
                DisplayName = invitation.DisplayName,
                ShopName = vendeur.ShopName,
                // CHIFFRÉ AVANT DE PARTIR. Le jeton traverse l'outbox puis Kafka ;
                // en clair, il faisait de ces deux-là des portes d'entrée dans la
                // boutique.
                ProtectedInvitationToken = _protecteur.Protect(token),
                ExpiresOnUtc = invitation.ExpiresOnUtc
            },
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result> Handle(RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(command.SellerId, command.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure(acteur.Error);
        }

        var invitation = await _invitations.GetByIdAsync(
            new SellerInvitationId(command.InvitationId), cancellationToken);

        if (invitation is null)
        {
            return Result.Failure(Error.NotFound("sellers.invitation.not_found", "Invitation introuvable."));
        }

        var revocation = invitation.Revoke(acteur.Value);
        if (revocation.IsFailure)
        {
            return revocation;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>LE SEUL CHEMIN QUI CRÉE UN MEMBRE, ET IL NE FAIT CONFIANCE À RIEN.</summary>
    public async Task<Result<Guid>> Handle(
        AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
        {
            return Result.Failure<Guid>(Error.Validation(
                "sellers.invitation.token_required", "Jeton d'invitation manquant."));
        }

        var invitation = await _invitations.GetByTokenHashAsync(
            _tokens.Hash(command.Token), cancellationToken);

        // Un jeton inconnu et un jeton révoqué se ressemblent volontairement : la
        // réponse ne doit pas aider à distinguer un lien périmé d'un lien inventé.
        if (invitation is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "sellers.invitation.not_found", "Invitation introuvable ou expirée."));
        }

        var compte = await _identity.GetUserAsync(command.UserId, cancellationToken);
        if (compte is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "sellers.seller.user_not_found", "Compte utilisateur introuvable."));
        }

        var acceptation = invitation.Accept(command.UserId, compte.Email, DateTime.UtcNow);
        if (acceptation.IsFailure)
        {
            // ON ENREGISTRE MÊME EN CAS D'ÉCHEC : `Accept` a pu poser le statut «
            // expirée », et cette information doit survivre à la requête, sinon
            // l'écran d'équipe affichera « en attente » pour toujours.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<Guid>(acceptation.Error);
        }

        if (await _members.GetMembershipAsync(
                invitation.SellerId, command.UserId, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "sellers.member.already_exists", "Ce compte fait déjà partie de l'équipe."));
        }

        var emetteur = await _members.GetMembershipAsync(
            invitation.SellerId, invitation.InvitedByUserId, cancellationToken);

        if (emetteur is null || !emetteur.CanAct)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "sellers.invitation.inviter_inactive",
                "La personne qui a émis cette invitation ne fait plus partie de l'équipe."));
        }

        var roles = await _roles.ListByIdsAsync([.. invitation.ReferencedRoleIds], cancellationToken);
        var parId = roles.ToDictionary(r => r.Id);

        // Un rôle personnalisé supprimé entre l'envoi et l'acceptation : on refuse
        // plutôt que d'admettre quelqu'un avec moins de droits que promis.
        if (parId.Count != invitation.ReferencedRoleIds.Count)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "sellers.invitation.role_missing",
                "Un des rôles de cette invitation n'existe plus. Demandez une nouvelle invitation."));
        }

        var rolesVendeur = invitation.SellerRoleIds.Select(id => parId[id]).ToArray();

        var affectations = invitation.StoreAssignments
            .Select(a => (a.StoreId, (IReadOnlyCollection<SellerRole>)a.RoleIds.Select(id => parId[id]).ToArray()))
            .ToArray();

        var membre = SellerMember.FromInvitation(invitation, rolesVendeur, affectations);
        if (membre.IsFailure)
        {
            return Result.Failure<Guid>(membre.Error);
        }

        await _members.AddAsync(membre.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return membre.Value.Id.Value;
    }

    // Cycle de vie d'un membre

    public async Task<Result> Handle(SetMemberRolesCommand command, CancellationToken cancellationToken)
    {
        var resolution = await ResoudreRolesAsync(command.RoleIds, [], cancellationToken);
        if (resolution.IsFailure)
        {
            return Result.Failure(resolution.Error);
        }

        // AUCUN CONTRÔLE DE PORTÉE ICI, ET C'EST LE CORRECTIF.

        return await MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, _) => membre.SetSellerRoles(acteur, resolution.Value.Vendeur));
    }

    public async Task<Result> Handle(AssignMemberStoreCommand command, CancellationToken cancellationToken)
    {
        // LA BOUTIQUE DOIT APPARTENIR AU VENDEUR (§36).
        var boutique = await _stores.GetByIdAsync(new StoreId(command.StoreId), cancellationToken);
        if (boutique is null || boutique.SellerId != command.SellerId)
        {
            return Result.Failure(Error.NotFound("sellers.store.not_found", "Boutique introuvable."));
        }

        var resolution = await ResoudreRolesAsync(command.RoleIds, [], cancellationToken);
        if (resolution.IsFailure)
        {
            return Result.Failure(resolution.Error);
        }

        // CES RÔLES VONT SUR UNE BOUTIQUE : c'est le geste que la garde vise.
        var portee = await EnsurePorteeBoutiqueAsync(
            command.SellerId, resolution.Value.Tous, cancellationToken);

        if (portee.IsFailure)
        {
            return portee;
        }

        // `Vendeur` porte ici les rôles de la boutique : la résolution range dans
        // ce champ tout ce qui n'est pas passé en affectation, et l'appel ci-dessus
        // n'en passe aucune.
        return await MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, _) => membre.AssignStore(acteur, command.StoreId, resolution.Value.Vendeur));
    }

    public Task<Result> Handle(UnassignMemberStoreCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, _) => membre.UnassignStore(acteur, command.StoreId));

    public Task<Result> Handle(SuspendMemberCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, contexte) => membre.Suspend(acteur, contexte.DernierProprietaire),
            avecDecomptes: true);

    public Task<Result> Handle(ReactivateMemberCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, _) => membre.Reactivate(acteur));

    public Task<Result> Handle(RevokeMemberCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, contexte) => membre.Revoke(
                acteur, contexte.DernierProprietaire, contexte.AutreAppartenance),
            avecDecomptes: true);

    /// <summary>Le départ volontaire. Voir <see cref="LeaveSellerCommand"/>.</summary>
    public Task<Result> Handle(LeaveSellerCommand command, CancellationToken cancellationToken)
        => _unitOfWork.ExecuteUnderSellerLockAsync(
            command.SellerId,
            ct => PartirAsync(command, ct),
            cancellationToken);

    private async Task<Result> PartirAsync(
        LeaveSellerCommand command, CancellationToken cancellationToken)
    {
        // ON NE PASSE PAS PAR `MuterAsync`, ET C'EST DÉLIBÉRÉ.
        var membre = await _members.GetMembershipAsync(
            command.SellerId, command.ActorUserId, cancellationToken);

        if (membre is null)
        {
            return Result.Failure(Error.NotFound(
                "sellers.member.not_found", "Vous ne faites pas partie de cette équipe."));
        }

        // Le verrou est tenu par `ExecuteUnderSellerLockAsync`, autour de tout ce
        // corps : l'invariant du dernier propriétaire se décide sur une lecture, et
        // deux départs simultanés la liraient périmée.
        var dernierProprietaire =
            await _members.CountActiveOwnersAsync(command.SellerId, cancellationToken) <= 1;

        var autreAppartenance =
            await _members.CountActiveMembershipsAsync(membre.UserId, cancellationToken) > 1;

        var depart = membre.Leave(dernierProprietaire, autreAppartenance);
        if (depart.IsFailure)
        {
            return depart;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    // Outillage

    /// <summary>Ce que le domaine ne peut pas savoir seul, compté AVANT la mutation.</summary>
    /// <param name="DernierProprietaire">
    /// Ce membre est-il le dernier propriétaire actif du vendeur ?
    /// </param>
    /// <param name="AutreAppartenance">
    /// Le compte appartient-il encore à une AUTRE équipe vendeur ? L'appartenance
    /// qu'on s'apprête à révoquer est encore comptée au moment de la lecture, d'où
    /// le <c> &gt; 1</c>.
    /// </param>
    private sealed record MutationContexte(bool DernierProprietaire, bool AutreAppartenance);

    /// <summary>Charge, vérifie l'appartenance, applique, enregistre.</summary>
    private Task<Result> MuterAsync(
        Guid sellerId,
        Guid actorUserId,
        Guid memberId,
        CancellationToken cancellationToken,
        Func<SellerMember, MemberActor, MutationContexte, Result> action,
        bool avecDecomptes = false)
        => _unitOfWork.ExecuteUnderSellerLockAsync(
            sellerId,
            ct => MuterSousVerrouAsync(sellerId, actorUserId, memberId, ct, action, avecDecomptes),
            cancellationToken);

    private async Task<Result> MuterSousVerrouAsync(
        Guid sellerId,
        Guid actorUserId,
        Guid memberId,
        CancellationToken cancellationToken,
        Func<SellerMember, MemberActor, MutationContexte, Result> action,
        bool avecDecomptes)
    {
        var acteur = await _acces.ResolveAsync(sellerId, actorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure(acteur.Error);
        }

        var membre = await _members.GetByIdAsync(new SellerMemberId(memberId), cancellationToken);

        // Le cloisonnement est vérifié une seconde fois dans l'agrégat ; ici il
        // évite de charger des rôles pour rien et rend le motif identique à «
        // membre inexistant », ce qui est la même information pour l'appelant.
        if (membre is null || membre.SellerId != sellerId)
        {
            return Result.Failure(Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        MutationContexte contexte;

        if (avecDecomptes)
        {
            // LE VERROU N'EST PAS DÉCORATIF : `xmin` NE VOIT PAS CETTE COURSE.
            contexte = new MutationContexte(
                DernierProprietaire:
                    await _members.CountActiveOwnersAsync(sellerId, cancellationToken) <= 1,
                AutreAppartenance:
                    await _members.CountActiveMembershipsAsync(membre.UserId, cancellationToken) > 1);
        }
        else
        {
            contexte = new MutationContexte(false, false);
        }

        var resultat = action(membre, acteur.Value, contexte);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private sealed record RolesResolus(
        IReadOnlyCollection<SellerRole> Vendeur,
        IReadOnlyCollection<(Guid StoreId, IReadOnlyCollection<SellerRole> Roles)> Boutiques,
        IReadOnlyCollection<SellerRole> Tous);

    /// <summary>UN IDENTIFIANT INCONNU EST UN REFUS, PAS UN SILENCE.</summary>
    private async Task<Result<RolesResolus>> ResoudreRolesAsync(
        IReadOnlyList<Guid> rolesVendeur,
        IReadOnlyList<StoreAssignmentInput> boutiques,
        CancellationToken cancellationToken)
    {
        var demandes = rolesVendeur
            .Concat(boutiques.SelectMany(b => b.RoleIds))
            .Distinct()
            .Select(id => new SellerRoleId(id))
            .ToArray();

        var trouves = await _roles.ListByIdsAsync(demandes, cancellationToken);
        var parId = trouves.ToDictionary(r => r.Id);

        var manquant = demandes.Where(id => !parId.ContainsKey(id)).Cast<SellerRoleId?>().FirstOrDefault();
        if (manquant is not null)
        {
            return Error.NotFound("sellers.role.not_found", "Rôle introuvable.");
        }

        var auVendeur = rolesVendeur.Distinct().Select(id => parId[new SellerRoleId(id)]).ToArray();

        var parBoutique = boutiques
            .Select(b => (
                b.StoreId,
                (IReadOnlyCollection<SellerRole>)b.RoleIds.Distinct()
                    .Select(id => parId[new SellerRoleId(id)]).ToArray()))
            .ToArray();

        return new RolesResolus(auVendeur, parBoutique, [.. trouves]);
    }

    /// <summary>LA DÉCISION D27, RESSERRÉE PAR LE LOT G.</summary>
    /// <param name="roles">
    /// Les rôles attachés À UNE BOUTIQUE par le geste en cours — jamais ceux du
    /// niveau vendeur.
    /// </param>
    private async Task<Result> EnsurePorteeBoutiqueAsync(
        Guid sellerId, IReadOnlyCollection<SellerRole> roles, CancellationToken cancellationToken)
    {
        // SEULS LES RÔLES ATTACHÉS À UNE BOUTIQUE SONT CONCERNÉS.
        var incadrables = roles
            .SelectMany(r => r.Permissions)
            .Where(p => !p.IsStoreScoped())
            .Distinct()
            .ToArray();

        if (incadrables.Length == 0)
        {
            return Result.Success();
        }

        // ET LE DÉCOMPTE DES BOUTIQUES N'EST LU QU'ENSUITE.
        var boutiques = await _stores.ListBySellerAsync(sellerId, cancellationToken);

        if (boutiques.Count <= 1)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Forbidden(
            "sellers.member.store_scope_unavailable",
            $"« {incadrables[0].ToCode()} » ne peut pas encore être cloisonnée par boutique : "
            + "l'attribuer donnerait accès à toutes vos boutiques. Retirez-la du rôle, ou "
            + "attribuez-la sur un dossier à boutique unique."));
    }
}
