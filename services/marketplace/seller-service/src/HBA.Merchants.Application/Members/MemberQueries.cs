using HBA.Merchants.Domain.Members;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

// ═════════════════════════════════════════════════════════════════════════════
// LES VUES DE L'ÉQUIPE.
//
// AUCUNE NE PORTE `TokenHash`, ET AUCUNE NE DOIT JAMAIS LE PORTER.
//
// Une empreinte rendue par une API est une empreinte qu'on peut comparer hors
// ligne : le jeton d'invitation n'a que trente-deux octets d'entropie, mais il
// n'y a aucune raison de donner à qui que ce soit de quoi vérifier une
// hypothèse. Ces enregistrements sont la frontière ; ils sont plats et explicites
// pour qu'un champ ajouté par mégarde se voie.
// ═════════════════════════════════════════════════════════════════════════════

public sealed record MemberRoleView(Guid RoleId, string Name, string Scope);

public sealed record MemberStoreView(
    Guid StoreId, string Status, string Enforcement, IReadOnlyList<MemberRoleView> Roles);

public sealed record MemberView(
    Guid Id,
    Guid UserId,
    string Status,
    string? DisplayName,
    string? JobTitle,
    bool IsOwner,
    DateTime? JoinedOnUtc,
    IReadOnlyList<MemberRoleView> Roles,
    IReadOnlyList<MemberStoreView> Stores,
    IReadOnlyList<string> EffectivePermissions);

public sealed record InvitationView(
    Guid Id,
    string Email,
    string Status,
    string? DisplayName,
    string? JobTitle,
    DateTime ExpiresOnUtc,
    DateTime CreatedOnUtc,
    IReadOnlyList<MemberRoleView> Roles);

public sealed record RoleView(
    Guid Id,
    string Name,
    string? Description,
    string Scope,
    bool IsSystemRole,
    IReadOnlyList<string> Permissions);

public sealed record PermissionView(string Code, string Domain, string Risk, bool OwnerOnly);

public sealed record ListMembersQuery(Guid SellerId, Guid ActorUserId) : IQuery<IReadOnlyList<MemberView>>;

public sealed record GetMemberQuery(Guid SellerId, Guid ActorUserId, Guid MemberId) : IQuery<MemberView>;

public sealed record ListInvitationsQuery(Guid SellerId, Guid ActorUserId)
    : IQuery<IReadOnlyList<InvitationView>>;

public sealed record ListSellerRolesQuery(Guid SellerId, Guid ActorUserId) : IQuery<IReadOnlyList<RoleView>>;

/// <summary>
/// Le catalogue des permissions.
/// </summary>
/// <remarks>
/// IL NE LIT PAS LA BASE, ET C'EST COHÉRENT AVEC LE RESTE DU MODULE.
///
/// La table `permissions` du §12 est une projection de l'énumération, semée au
/// démarrage. La source étant le code, la rendre depuis le code évite qu'un écran
/// affiche un catalogue périmé sur une base dont l'amorçage n'a pas tourné.
/// </remarks>
public sealed record ListPermissionsQuery : IQuery<IReadOnlyList<PermissionView>>;

internal sealed class MemberQueryHandler :
    IQueryHandler<ListMembersQuery, IReadOnlyList<MemberView>>,
    IQueryHandler<GetMemberQuery, MemberView>,
    IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationView>>,
    IQueryHandler<ListSellerRolesQuery, IReadOnlyList<RoleView>>,
    IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionView>>
{
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRoleRepository _roles;
    private readonly ISellerInvitationRepository _invitations;
    private readonly MemberAccessResolver _acces;

    public MemberQueryHandler(
        ISellerMemberRepository members,
        ISellerRoleRepository roles,
        ISellerInvitationRepository invitations,
        MemberAccessResolver acces)
    {
        _members = members;
        _roles = roles;
        _invitations = invitations;
        _acces = acces;
    }

    public async Task<Result<IReadOnlyList<MemberView>>> Handle(
        ListMembersQuery query, CancellationToken cancellationToken)
    {
        var garde = await AutoriserAsync(query.SellerId, query.ActorUserId, MerchantPermission.MemberView, cancellationToken);
        if (garde.IsFailure)
        {
            return Result.Failure<IReadOnlyList<MemberView>>(garde.Error);
        }

        var membres = await _members.ListBySellerAsync(query.SellerId, cancellationToken);
        var catalogue = await ChargerCatalogueAsync(query.SellerId, cancellationToken);

        return Result.Success<IReadOnlyList<MemberView>>(
            [.. membres.Select(m => Projeter(m, catalogue))]);
    }

    public async Task<Result<MemberView>> Handle(GetMemberQuery query, CancellationToken cancellationToken)
    {
        var garde = await AutoriserAsync(query.SellerId, query.ActorUserId, MerchantPermission.MemberView, cancellationToken);
        if (garde.IsFailure)
        {
            return Result.Failure<MemberView>(garde.Error);
        }

        var membre = await _members.GetByIdAsync(new SellerMemberId(query.MemberId), cancellationToken);

        if (membre is null || membre.SellerId != query.SellerId)
        {
            return Result.Failure<MemberView>(
                Error.NotFound("sellers.member.not_found", "Membre introuvable."));
        }

        var catalogue = await ChargerCatalogueAsync(query.SellerId, cancellationToken);
        return Projeter(membre, catalogue);
    }

    public async Task<Result<IReadOnlyList<InvitationView>>> Handle(
        ListInvitationsQuery query, CancellationToken cancellationToken)
    {
        var garde = await AutoriserAsync(query.SellerId, query.ActorUserId, MerchantPermission.MemberView, cancellationToken);
        if (garde.IsFailure)
        {
            return Result.Failure<IReadOnlyList<InvitationView>>(garde.Error);
        }

        var invitations = await _invitations.ListBySellerAsync(query.SellerId, cancellationToken);
        var catalogue = await ChargerCatalogueAsync(query.SellerId, cancellationToken);

        return Result.Success<IReadOnlyList<InvitationView>>(
        [
            .. invitations.Select(i => new InvitationView(
                i.Id.Value, i.Email, i.Status.ToString(), i.DisplayName, i.JobTitle,
                i.ExpiresOnUtc, i.CreatedOnUtc,
                [.. i.ReferencedRoleIds.Select(id => Nommer(id, catalogue))]))
        ]);
    }

    public async Task<Result<IReadOnlyList<RoleView>>> Handle(
        ListSellerRolesQuery query, CancellationToken cancellationToken)
    {
        var garde = await AutoriserAsync(query.SellerId, query.ActorUserId, MerchantPermission.RoleView, cancellationToken);
        if (garde.IsFailure)
        {
            return Result.Failure<IReadOnlyList<RoleView>>(garde.Error);
        }

        var roles = await _roles.ListAvailableAsync(query.SellerId, cancellationToken);

        return Result.Success<IReadOnlyList<RoleView>>(
        [
            .. roles.Select(r => new RoleView(
                r.Id.Value, r.Name, r.Description, r.Scope.ToString(), r.IsSystemRole,
                [.. r.Permissions.Select(p => p.ToCode()).Order()]))
        ]);
    }

    public Task<Result<IReadOnlyList<PermissionView>>> Handle(
        ListPermissionsQuery query, CancellationToken cancellationToken)
        => Task.FromResult(Result.Success<IReadOnlyList<PermissionView>>(
        [
            .. MerchantPermissions.All.Select(p => new PermissionView(
                p.ToCode(), Domaine(p), p.RiskOf().ToString(), p.IsOwnerOnly()))
        ]));

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    private async Task<Result> AutoriserAsync(
        Guid sellerId, Guid actorUserId, MerchantPermission requise, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(sellerId, actorUserId, cancellationToken);

        return acteur.IsFailure ? Result.Failure(acteur.Error) : acteur.Value.Ensure(requise);
    }

    private async Task<IReadOnlyDictionary<SellerRoleId, SellerRole>> ChargerCatalogueAsync(
        Guid sellerId, CancellationToken cancellationToken)
        => (await _roles.ListAvailableAsync(sellerId, cancellationToken)).ToDictionary(r => r.Id);

    private static MemberView Projeter(
        SellerMember membre, IReadOnlyDictionary<SellerRoleId, SellerRole> catalogue)
        => new(
            membre.Id.Value,
            membre.UserId,
            membre.Status.ToString(),
            membre.DisplayName,
            membre.JobTitle,
            membre.IsOwner,
            membre.JoinedOnUtc,
            [.. membre.SellerRoleIds.Select(id => Nommer(id, catalogue))],
            [.. membre.StoreMemberships.Select(s => new MemberStoreView(
                s.StoreId, s.Status.ToString(), s.Enforcement.ToString(),
                [.. s.RoleIds.Select(id => Nommer(id, catalogue))]))],
            [.. membre.EffectivePermissions([.. catalogue.Values]).Select(p => p.ToCode()).Order()]);

    /// <summary>
    /// UN RÔLE ABSENT DU CATALOGUE EST NOMMÉ, PAS OMIS.
    ///
    /// Le cas ne devrait pas se produire — un rôle encore porté ne se supprime
    /// pas. S'il se produisait, omettre la ligne ferait disparaître un droit de
    /// l'écran tout en le laissant actif en base, et personne ne chercherait la
    /// cause du côté des rôles.
    /// </summary>
    private static MemberRoleView Nommer(
        SellerRoleId id, IReadOnlyDictionary<SellerRoleId, SellerRole> catalogue)
        => catalogue.TryGetValue(id, out var role)
            ? new MemberRoleView(id.Value, role.Name, role.Scope.ToString())
            : new MemberRoleView(id.Value, "(rôle introuvable)", RoleScope.Seller.ToString());

    /// <summary>Le domaine d'une permission, déduit de son code — <c>ORDER_CONFIRM</c> → <c>ORDER</c>.</summary>
    private static string Domaine(MerchantPermission permission)
    {
        var code = permission.ToCode();
        var separateur = code.IndexOf('_');

        return separateur > 0 ? code[..separateur] : code;
    }
}
