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

/// <summary>
/// Ce que rend une invitation émise ou renvoyée.
/// </summary>
/// <remarks>
/// LE JETON EST RENDU À L'APPELANT, ET UNE SEULE FOIS.
///
/// Il n'est stocké nulle part — la base ne retient que son empreinte — donc cette
/// réponse est le seul moment où il existe en clair côté plateforme. L'appelant
/// est le propriétaire qui vient de créer l'invitation : lui rendre le lien est ce
/// qui lui permet de le transmettre par ses propres moyens, et de le retrouver si
/// le courriel se perd.
///
/// Il ne figure DÉLIBÉRÉMENT dans aucun événement : ceux-ci traversent l'outbox,
/// Kafka et les journaux de plusieurs services, tous conçus pour être relus.
/// </remarks>
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

/// <summary>
/// L'acceptation.
/// </summary>
/// <remarks>
/// NI `SellerId` NI ADRESSE DANS CETTE COMMANDE, ET C'EST LE POINT.
///
/// Le jeton désigne l'invitation, l'invitation désigne le vendeur, et l'adresse
/// est lue chez identity. Rien de ce qui décide n'est fourni par l'appelant —
/// c'est exactement ce que demande le §36 : « sellerId/storeId provenant du body
/// ne constitue jamais une preuve d'autorisation ».
/// </remarks>
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

/// <summary>
/// Le départ volontaire — le seul geste qu'un membre pose sur lui-même.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// `SellerMember.Leave` EXISTAIT SANS AUCUN APPELANT.
///
/// Écrite, testée, protégée contre le départ du dernier propriétaire — et
/// injoignable : aucune commande, aucune route. Pour quitter une équipe, il fallait
/// demander à quelqu'un d'autre de vous révoquer. Le statut `Left` n'était donc
/// jamais atteignable en production, et `Reactivate` testait un état mort.
///
/// AUCUN `MemberId` DANS CETTE COMMANDE, ET C'EST LE POINT.
///
/// On ne quitte que sa propre appartenance. La faire porter un identifiant de
/// membre en ferait une révocation déguisée, sans permission à exiger — c'est-à-dire
/// le contournement exact de `MEMBER_REVOKE`. Le membre est résolu depuis le JETON.
///
/// ET AUCUNE PERMISSION N'EST EXIGÉE.
///
/// Partir n'est pas un droit qui se délègue : c'est le seul geste que même un
/// membre suspendu doit pouvoir poser. La seule limite est l'invariant du dernier
/// propriétaire, que l'agrégat oppose.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record LeaveSellerCommand(Guid SellerId, Guid ActorUserId) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES COMMANDES DE L'ÉQUIPE — UN SEUL HANDLER, COMME POUR LES BOUTIQUES.
///
/// TROIS CONTRÔLES REVIENNENT DANS PRESQUE TOUTES, ET AUCUN N'EST OPTIONNEL.
///
///   1. RÉSOUDRE L'ACTEUR — qui parle, et avec quels droits. C'est la garde ;
///      il n'y en a pas d'autre en amont (voir `MemberAccessResolver`).
///   2. RÉSOUDRE LES RÔLES DEMANDÉS — et refuser si l'un manque. Ignorer un
///      identifiant inconnu attribuerait silencieusement moins que demandé, et
///      personne ne s'en apercevrait avant le premier refus inexpliqué.
///   3. LA PORTÉE BOUTIQUE (D27) — refuser un rôle à vocation boutique dès que le
///      vendeur en a deux, parce qu'il s'appliquerait alors aux deux.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Invitations
    // ═════════════════════════════════════════════════════════════════════════

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
        //
        // Deux invitations en attente pour la même personne, ce sont deux jetons
        // valides et deux jeux de rôles concurrents : celui qui aboutit dépend du
        // lien que l'invité ouvre en premier. On renvoie plutôt à la relance.
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
        //
        // L'agrégat ne porte que des identifiants et n'interroge aucun dépôt. Sans
        // cette lecture, `Refresh` ne pourrait pas vérifier que le relanceur détient
        // lui-même ce que l'invitation promet — et `MEMBER_INVITE` seul suffisait à
        // ressusciter une invitation SELLER_ADMIN, jeton en clair compris.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ANNONCE QUI PORTE LE JETON — LE SEUL ÉVÉNEMENT PUBLIÉ HORS DU DOMAINE.
    ///
    /// POURQUOI ICI ET NON DANS UN HANDLER D'ÉVÉNEMENT DE DOMAINE.
    ///
    /// L'agrégat ne connaît pas le jeton : il ne reçoit que son empreinte, et c'est
    /// tout l'intérêt du §7. Le faire remonter par un événement de domaine
    /// obligerait à le loger dans l'agrégat sans le persister — un champ dont
    /// l'unique raison d'être serait de contourner sa propre conception. Ce
    /// handler est le seul endroit où le secret existe.
    ///
    /// PUBLIÉ AVANT `SaveChanges`, DONC DANS LA MÊME TRANSACTION.
    ///
    /// `PublishAsync` met en file ; c'est `SaveChanges` qui écrit les lignes
    /// d'outbox. Publier après enregistrerait l'invitation sans l'annonce en cas
    /// de panne entre les deux : une invitation qui existe et que personne ne
    /// reçoit — invisible, puisque tout aurait l'air d'avoir fonctionné.
    ///
    /// ET LE NOM DE LA BOUTIQUE VOYAGE, PARCE QUE L'INVITÉ DOIT SAVOIR QUI L'INVITE.
    ///
    /// Un courriel qui dit « vous avez été invité » sans dire par qui est
    /// indiscernable d'un hameçonnage — et c'est le seul e-mail de la plateforme
    /// qui demande à quelqu'un d'ouvrir un lien vers un compte qu'il n'a pas
    /// encore.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
                // boutique. Seul notification-service le rouvre.
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

    /// <summary>
    /// LE SEUL CHEMIN QUI CRÉE UN MEMBRE, ET IL NE FAIT CONFIANCE À RIEN.
    ///
    /// Le jeton est haché puis cherché — la valeur en clair ne descend jamais
    /// jusqu'à la base. L'adresse vient d'identity, jamais de la requête. Et
    /// l'invitation ne survit pas à celui qui l'a émise : un gérant révoqué ne
    /// laisse pas derrière lui des recrutements qui aboutissent encore.
    /// </summary>
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
            // ON ENREGISTRE MÊME EN CAS D'ÉCHEC : `Accept` a pu poser le statut
            // « expirée », et cette information doit survivre à la requête, sinon
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

    // ═════════════════════════════════════════════════════════════════════════
    // Cycle de vie d'un membre
    // ═════════════════════════════════════════════════════════════════════════

    public async Task<Result> Handle(SetMemberRolesCommand command, CancellationToken cancellationToken)
    {
        var resolution = await ResoudreRolesAsync(command.RoleIds, [], cancellationToken);
        if (resolution.IsFailure)
        {
            return Result.Failure(resolution.Error);
        }

        // AUCUN CONTRÔLE DE PORTÉE ICI, ET C'EST LE CORRECTIF.
        //
        // `SetMemberRoles` attribue AU NIVEAU DU VENDEUR. Un droit donné à ce niveau
        // est un choix explicite — le vendeur veut que ce membre agisse sur tout le
        // dossier — et il ne promet aucun cloisonnement qu'il faudrait tenir. La
        // version précédente le refusait dès la deuxième boutique, ce qui interdisait
        // par exemple de nommer un administrateur général chez un vendeur à deux
        // magasins.

        return await MuterAsync(
            command.SellerId, command.ActorUserId, command.MemberId, cancellationToken,
            (membre, acteur, _) => membre.SetSellerRoles(acteur, resolution.Value.Vendeur));
    }

    public async Task<Result> Handle(AssignMemberStoreCommand command, CancellationToken cancellationToken)
    {
        // LA BOUTIQUE DOIT APPARTENIR AU VENDEUR (§36).
        //
        // Sans ce contrôle, un propriétaire affecterait son employé à la boutique
        // d'un concurrent — et le jour où le cadrage par boutique s'appliquera,
        // cette ligne écrite aujourd'hui deviendrait un accès réel.
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
    /// <remarks>
    /// TOUT LE CORPS PASSE SOUS VERROU, Y COMPRIS LA PREMIÈRE LECTURE.
    ///
    /// Le verrou était pris au MILIEU du handler, après avoir chargé le membre — et
    /// il ne verrouillait rien, faute de transaction ouverte. Voir l'encadré
    /// d'`ISellerUnitOfWork.ExecuteUnderSellerLockAsync`.
    /// </remarks>
    public Task<Result> Handle(LeaveSellerCommand command, CancellationToken cancellationToken)
        => _unitOfWork.ExecuteUnderSellerLockAsync(
            command.SellerId,
            ct => PartirAsync(command, ct),
            cancellationToken);

    private async Task<Result> PartirAsync(
        LeaveSellerCommand command, CancellationToken cancellationToken)
    {
        // ON NE PASSE PAS PAR `MuterAsync`, ET C'EST DÉLIBÉRÉ.
        //
        // Celui-ci résout un ACTEUR puis charge une CIBLE. Ici les deux sont la même
        // personne, et surtout : `MemberAccessResolver` refuse un membre qui ne peut
        // pas agir — un suspendu ne pourrait donc jamais partir, ce qui est
        // exactement le contraire de ce que cette commande doit permettre.
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ce que le domaine ne peut pas savoir seul, compté AVANT la mutation.
    /// </summary>
    /// <param name="DernierProprietaire">Ce membre est-il le dernier propriétaire actif du vendeur ?</param>
    /// <param name="AutreAppartenance">
    /// Le compte appartient-il encore à une AUTRE équipe vendeur ? L'appartenance
    /// qu'on s'apprête à révoquer est encore comptée au moment de la lecture, d'où
    /// le <c>&gt; 1</c>.
    /// </param>
    private sealed record MutationContexte(bool DernierProprietaire, bool AutreAppartenance);

    /// <summary>
    /// Charge, vérifie l'appartenance, applique, enregistre.
    /// </summary>
    /// <remarks>
    /// LES DÉCOMPTES PASSENT PAR UN ARGUMENT, PAS PAR UN CHAMP DU HANDLER.
    ///
    /// Un champ mutable partagé aurait fonctionné — le handler est « scoped », donc
    /// une commande à la fois. Il aurait aussi rendu le résultat dépendant d'un
    /// ordre d'affectation invisible dans la signature : le genre de couplage qui
    /// survit à une refonte et casse en silence.
    ///
    /// ET ILS SONT PRIS AVANT LA MUTATION, CE QUI N'EST PAS UN DÉTAIL.
    ///
    /// `ModuleDbContext` dépêche les événements de domaine AVANT d'enregistrer.
    /// Un décompte pris après coup — ou pire, depuis un gestionnaire d'événement —
    /// lirait l'état d'AVANT et répondrait toujours la même chose. Ici, le membre
    /// visé est encore actif quand on compte : c'est justement ce qui rend le
    /// « en a-t-il une autre » lisible comme `> 1`.
    ///
    /// Les deux décomptes sont pris ensemble, même quand une seule mutation les
    /// utilise. Deux requêtes indexées valent mieux qu'un drapeau de plus à
    /// oublier — et `Suspend` finira par avoir besoin du second le jour où une
    /// suspension retirera aussi le rôle.
    /// </remarks>
    /// <remarks>
    /// LE VERROU ENVELOPPE TOUT, Y COMPRIS LA RÉSOLUTION DE L'ACTEUR.
    ///
    /// Il était pris au milieu de ce corps, juste avant les décomptes — et il ne
    /// verrouillait rien, faute de transaction ouverte. Deux corrections, pas une :
    /// il est désormais réellement tenu, et il couvre AUSSI les lectures qui le
    /// précédaient. Décider qui agit sur une lecture hors verrou, puis verrouiller
    /// pour lire le reste, laissait la moitié de la décision exposée.
    ///
    /// IL EST PRIS MÊME SANS DÉCOMPTES (`avecDecomptes: false`). Le coût est un
    /// verrou consultatif par mutation d'équipe — quelques microsecondes, et une
    /// sérialisation qui ne concerne qu'un seul commerçant. Le prix d'un verrou
    /// conditionnel serait qu'un futur appelant oublie la condition.
    /// </remarks>
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
        // évite de charger des rôles pour rien et rend le motif identique à
        // « membre inexistant », ce qui est la même information pour l'appelant.
        if (membre is null || membre.SellerId != sellerId)
        {
            return Result.Failure(Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        MutationContexte contexte;

        if (avecDecomptes)
        {
            // ═════════════════════════════════════════════════════════════════
            // LE VERROU N'EST PAS DÉCORATIF : `xmin` NE VOIT PAS CETTE COURSE.
            //
            // La configuration EF affirmait que le verrou optimiste l'attrapait —
            // « deux propriétaires qui se retirent simultanément liraient chacun
            // "il en reste deux" ; xmin fait échouer la seconde écriture ». C'est
            // faux, et de la façon la plus coûteuse qui soit : `xmin` est un jeton
            // PAR LIGNE. Révoquer O1 et O2 écrit DEUX LIGNES DIFFÉRENTES — aucun
            // conflit de concurrence n'existe, les deux écritures réussissent.
            //
            // Chacune a lu `CountActiveOwnersAsync == 2` avant sa mutation, en
            // `Read Committed`. Le vendeur tombe à ZÉRO propriétaire actif. Et
            // comme `EnsureCanAdminister` exige `acteur.IsOwner` pour toucher un
            // propriétaire, plus personne ne peut réactiver qui que ce soit : le
            // dossier est définitivement inadministrable.
            //
            // UN VERROU CONSULTATIF, ET NON `SELECT … FOR UPDATE`.
            //
            // Il n'y a pas de ligne commune à verrouiller — c'est tout le problème.
            // Le verrou porte donc sur le VENDEUR, pris pour la durée de la
            // transaction et relâché par PostgreSQL au `COMMIT` comme au
            // `ROLLBACK`. Il sérialise les mutations d'équipe d'un même vendeur, et
            // uniquement d'un même vendeur : deux commerçants différents ne
            // s'attendent jamais.
            //
            // IL EST PRIS AVANT LE DÉCOMPTE, JAMAIS APRÈS.
            //
            // Après, il ne protégerait plus rien : la lecture périmée aurait déjà
            // eu lieu, et le verrou ne ferait que sérialiser deux décisions déjà
            // prises sur le même état faux.
            //
            // ET IL N'EST PLUS PRIS ICI DU TOUT — il enveloppe désormais ce corps
            // entier, depuis `MuterAsync`. Deux raisons, et la première est que le
            // verrou pris à cet endroit NE VERROUILLAIT RIEN : sans transaction
            // ouverte, PostgreSQL le relâchait aussitôt. La seconde est que même
            // réparé sur place, il aurait laissé hors de sa portée la résolution de
            // l'acteur et le chargement du membre, qui précèdent — donc la moitié
            // des lectures dont la décision dépend.
            // ═════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// UN IDENTIFIANT INCONNU EST UN REFUS, PAS UN SILENCE.
    ///
    /// Ignorer les identifiants non résolus attribuerait moins que demandé sans
    /// que rien ne le signale : le propriétaire croirait avoir délégué, l'employé
    /// se heurterait à des refus, et l'écran afficherait la liste qu'on lui a
    /// donnée. C'est la panne dont personne ne trouve la cause.
    /// </summary>
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA DÉCISION D27, RESSERRÉE PAR LE LOT G.
    ///
    /// CE QUE LA RÈGLE ÉTAIT, ET POURQUOI ELLE NE POUVAIT PAS RESTER AINSI.
    ///
    /// Elle refusait TOUT rôle de vocation boutique dès la deuxième boutique. Le
    /// motif était juste — un rôle boutique s'appliquait au vendeur entier, donc
    /// un ORDER_MANAGER recruté pour la boutique B agissait sur la A — mais la
    /// portée était trop large : elle interdisait aussi les rôles purement
    /// catalogue, alors que catalog-service cadre RÉELLEMENT depuis le lot F.
    ///
    /// Concrètement, un vendeur à deux magasins ne pouvait pas donner
    /// CATALOG_MANAGER à qui que ce soit, alors même que ce rôle est désormais
    /// cloisonné offre par offre et fiche par fiche.
    ///
    /// LA RÈGLE PORTE MAINTENANT SUR LES PERMISSIONS, PAS SUR LA VOCATION.
    ///
    /// Ce qui compte n'est pas l'étiquette `RoleScope.Store` — c'est de savoir si
    /// CHAQUE permission du rôle est appliquée avec un identifiant de boutique
    /// quelque part. `MerchantPermissions.StoreScoped` porte cette liste, et son
    /// encadré dit précisément pourquoi `INVENTORY_*` et `ORDER_*` n'y sont pas :
    /// un lieu d'expédition est une infrastructure de vendeur, et une commande
    /// peut mêler plusieurs boutiques. Ce ne sont pas des oublis.
    ///
    /// LE REFUS NOMME LA PERMISSION FAUTIVE.
    ///
    /// « Ce rôle n'est pas cloisonnable » n'aide personne. « INVENTORY_ADJUST ne
    /// l'est pas encore » dit au vendeur quoi retirer de son rôle personnalisé
    /// pour que l'attribution passe — et c'est une réponse actionnable, ce que
    /// l'ancienne n'était pas.
    ///
    /// LE PENDANT DE `CreateStoreCommand` DOIT SUIVRE LA MÊME RÈGLE.
    ///
    /// Ouvrir une deuxième boutique est refusé tant qu'un membre actif porte un
    /// rôle non cloisonnable. Les deux gardes vont par paire : desserrer celle-ci
    /// sans desserrer l'autre laisserait le vendeur bloqué par l'autre bout.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="roles">
    /// Les rôles attachés À UNE BOUTIQUE par le geste en cours — jamais ceux du
    /// niveau vendeur. Voir l'encadré du corps.
    /// </param>
    private async Task<Result> EnsurePorteeBoutiqueAsync(
        Guid sellerId, IReadOnlyCollection<SellerRole> roles, CancellationToken cancellationToken)
    {
        // SEULS LES RÔLES ATTACHÉS À UNE BOUTIQUE SONT CONCERNÉS.
        //
        // Un droit donné AU NIVEAU DU VENDEUR est un choix explicite — le vendeur a
        // voulu que ce comptable voie les finances de toute l'entreprise, et il n'y
        // a rien à protéger. Un droit donné VIA UNE AFFECTATION porte une promesse
        // de cloisonnement que le code ne sait pas tenir pour `INVENTORY_*` et
        // `ORDER_*` : c'est elle, et elle seule, qu'on refuse de faire à moitié.
        //
        // La version précédente regardait TOUS les rôles, y compris ceux du niveau
        // vendeur. Le pendant de `StoreCommandHandler` faisait la même erreur, et
        // elle y était fatale : le propriétaire portant OWNER, plus aucun vendeur ne
        // pouvait ouvrir sa deuxième boutique.
        //
        // ET LA VOCATION `RoleScope` N'ENTRE PAS DANS LA DÉCISION.
        //
        // Elle dit où le vendeur COMPTE employer le rôle, pas ce que le code sait
        // cloisonner. Un rôle personnalisé de vocation `Seller` affecté à une
        // boutique pose exactement le même problème qu'un rôle marqué `Store`.
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
        //
        // C'est une requête ; la faire d'abord la ferait payer à toutes les
        // attributions, y compris les innombrables cas mono-boutique. L'ordre est
        // ici une décision de coût, pas de sécurité — les deux conditions se
        // multiplient, leur ordre n'y change rien.
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
