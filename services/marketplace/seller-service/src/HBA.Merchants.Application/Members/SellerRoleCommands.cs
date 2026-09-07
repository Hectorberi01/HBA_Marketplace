using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Stores;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>Crée un rôle taillé par le vendeur.</summary>
public sealed record CreateSellerRoleCommand(
    Guid SellerId,
    Guid ActorUserId,
    string Name,
    string? Description,
    string? Scope,
    IReadOnlyList<string> Permissions) : ICommand<Guid>;

/// <summary>Réécrit un rôle personnalisé.</summary>
public sealed record UpdateSellerRoleCommand(
    Guid SellerId,
    Guid ActorUserId,
    Guid RoleId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand;

public sealed record DeleteSellerRoleCommand(Guid SellerId, Guid ActorUserId, Guid RoleId) : ICommand;

/// <summary>LES RÔLES PERSONNALISÉS — CE QUE CE HANDLER GARDE, EN PLUS DE LA PERMISSION.</summary>
internal sealed class SellerRoleCommandHandler :
    ICommandHandler<CreateSellerRoleCommand, Guid>,
    ICommandHandler<UpdateSellerRoleCommand>,
    ICommandHandler<DeleteSellerRoleCommand>
{
    private readonly ISellerRoleRepository _roles;
    private readonly ISellerMemberRepository _members;
    private readonly IStoreRepository _stores;
    private readonly MemberAccessResolver _acces;
    private readonly ISellerUnitOfWork _unitOfWork;

    public SellerRoleCommandHandler(
        ISellerRoleRepository roles,
        ISellerMemberRepository members,
        IStoreRepository stores,
        MemberAccessResolver acces,
        ISellerUnitOfWork unitOfWork)
    {
        _roles = roles;
        _members = members;
        _stores = stores;
        _acces = acces;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(
        CreateSellerRoleCommand command, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(command.SellerId, command.ActorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<Guid>(acteur.Error);
        }

        var habilitation = acteur.Value.Ensure(MerchantPermission.RoleCreate);
        if (habilitation.IsFailure)
        {
            return Result.Failure<Guid>(habilitation.Error);
        }

        var demandees = Traduire(command.Permissions);
        if (demandees.IsFailure)
        {
            return Result.Failure<Guid>(demandees.Error);
        }

        var portee = LirePortee(command.Scope);
        if (portee.IsFailure)
        {
            return Result.Failure<Guid>(portee.Error);
        }

        // L'UNICITÉ DU NOM SE VÉRIFIE ICI ET NON DANS L'AGRÉGAT.
        if (await _roles.NameExistsAsync(command.SellerId, command.Name.Trim(), cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "sellers.role.name_taken", "Un rôle porte déjà ce nom."));
        }

        var role = SellerRole.Custom(
            command.SellerId,
            command.Name,
            command.Description,
            portee.Value,
            acteur.Value.Permissions,
            demandees.Value);

        if (role.IsFailure)
        {
            return Result.Failure<Guid>(role.Error);
        }

        await _roles.AddAsync(role.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return role.Value.Id.Value;
    }

    public async Task<Result> Handle(UpdateSellerRoleCommand command, CancellationToken cancellationToken)
    {
        var contexte = await ChargerAsync(
            command.SellerId, command.ActorUserId, command.RoleId,
            MerchantPermission.RoleUpdate, cancellationToken);

        if (contexte.IsFailure)
        {
            return Result.Failure(contexte.Error);
        }

        var (acteur, role) = contexte.Value;

        var demandees = Traduire(command.Permissions);
        if (demandees.IsFailure)
        {
            return Result.Failure(demandees.Error);
        }

        var nom = command.Name.Trim();

        // ON NE VÉRIFIE L'UNICITÉ QUE SI LE NOM CHANGE.
        if (!string.Equals(nom, role.Name, StringComparison.Ordinal)
            && await _roles.NameExistsAsync(command.SellerId, nom, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "sellers.role.name_taken", "Un rôle porte déjà ce nom."));
        }

        // LA PORTÉE NE FIGURE PAS DANS CETTE COMMANDE, ET C'EST DÉLIBÉRÉ.
        var portee = await EnsurePorteeBoutiqueAsync(
            command.SellerId, new SellerRoleId(command.RoleId), demandees.Value, cancellationToken);

        if (portee.IsFailure)
        {
            return portee;
        }

        var mise = role.Update(nom, command.Description, demandees.Value, acteur.Permissions);
        if (mise.IsFailure)
        {
            return mise;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(DeleteSellerRoleCommand command, CancellationToken cancellationToken)
    {
        var contexte = await ChargerAsync(
            command.SellerId, command.ActorUserId, command.RoleId,
            MerchantPermission.RoleDelete, cancellationToken);

        if (contexte.IsFailure)
        {
            return Result.Failure(contexte.Error);
        }

        var (acteur, role) = contexte.Value;

        // LE DÉCOMPTE EST LU AVANT, ET LA DÉLÉGATION EST VÉRIFIÉE AVEC.
        var porteurs = await _members.CountByRoleAsync(
            command.SellerId, new SellerRoleId(command.RoleId), cancellationToken);

        var suppression = role.EnsureDeletable(porteurs, acteur.Permissions);
        if (suppression.IsFailure)
        {
            return suppression;
        }

        _roles.Remove(role);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // Outillage

    /// <summary>
    /// Refuse d'ajouter à un rôle DÉJÀ AFFECTÉ À UNE BOUTIQUE une permission que le
    /// code ne sait pas cloisonner, dès lors que le vendeur a plus d'une boutique.
    /// </summary>
    private async Task<Result> EnsurePorteeBoutiqueAsync(
        Guid sellerId,
        SellerRoleId roleId,
        IReadOnlyCollection<MerchantPermission> demandees,
        CancellationToken cancellationToken)
    {
        var incadrable = demandees
            .Where(p => !p.IsStoreScoped())
            .Cast<MerchantPermission?>()
            .FirstOrDefault();

        if (incadrable is not { } bloquante)
        {
            return Result.Success();
        }

        // LES DEUX LECTURES NE PARTENT QU'APRÈS LE TEST CI-DESSUS.
        var boutiques = await _stores.ListBySellerAsync(sellerId, cancellationToken);
        if (boutiques.Count <= 1)
        {
            return Result.Success();
        }

        var membres = await _members.ListBySellerAsync(sellerId, cancellationToken);

        var affecte = membres
            .Where(m => m.CanAct)
            .SelectMany(m => m.StoreMemberships.Where(a => a.Status == StoreMembershipStatus.Active))
            .Any(a => a.RoleIds.Contains(roleId));

        return affecte
            ? Result.Failure(Error.Forbidden(
                "sellers.role.store_scope_unavailable",
                $"Ce rôle est affecté à une boutique, et « {bloquante.ToCode()} » ne peut pas encore être "
                + "cloisonnée : l'ajouter donnerait accès à toutes vos boutiques. Retirez-la, ou "
                + "détachez d'abord ce rôle des boutiques auxquelles il est affecté."))
            : Result.Success();
    }

    /// <summary>
    /// Résout l'acteur, contrôle sa permission, et charge le rôle visé — en
    /// refusant celui d'un AUTRE vendeur.
    /// </summary>
    private async Task<Result<(MemberActor Acteur, SellerRole Role)>> ChargerAsync(
        Guid sellerId, Guid actorUserId, Guid roleId,
        MerchantPermission requise, CancellationToken cancellationToken)
    {
        var acteur = await _acces.ResolveAsync(sellerId, actorUserId, cancellationToken);
        if (acteur.IsFailure)
        {
            return Result.Failure<(MemberActor, SellerRole)>(acteur.Error);
        }

        var habilitation = acteur.Value.Ensure(requise);
        if (habilitation.IsFailure)
        {
            return Result.Failure<(MemberActor, SellerRole)>(habilitation.Error);
        }

        var role = await _roles.GetByIdAsync(new SellerRoleId(roleId), cancellationToken);

        if (role is null || role.SellerId != sellerId)
        {
            return Result.Failure<(MemberActor, SellerRole)>(Error.NotFound(
                "sellers.role.not_found", "Rôle introuvable."));
        }

        return (acteur.Value, role);
    }

    /// <summary>Traduit les codes publics en permissions.</summary>
    private static Result<IReadOnlyCollection<MerchantPermission>> Traduire(IReadOnlyList<string> codes)
    {
        if (codes is null || codes.Count == 0)
        {
            return Error.Validation(
                "sellers.role.permissions_required", "Un rôle sans permission ne sert à rien.");
        }

        var resolues = new List<MerchantPermission>(codes.Count);

        foreach (var code in codes)
        {
            if (MerchantPermissions.Parse(code?.Trim()) is not { } permission)
            {
                return Error.Validation(
                    "sellers.role.permission_unknown", $"Permission inconnue : « {code} ».");
            }

            resolues.Add(permission);
        }

        return resolues;
    }

    /// <summary>Lit la vocation demandée.</summary>
    private static Result<RoleScope> LirePortee(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return RoleScope.Seller;
        }

        // `Enum.IsDefined` EN PLUS DE `TryParse`, ET CE N'EST PAS REDONDANT.
        return Enum.TryParse<RoleScope>(scope.Trim(), ignoreCase: true, out var portee)
            && Enum.IsDefined(portee)
            ? portee
            : Error.Validation("sellers.role.scope_invalid", $"Portée inconnue : « {scope} ».");
    }
}
