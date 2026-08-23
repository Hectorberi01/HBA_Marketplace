using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Stores;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Application.Members;

/// <summary>
/// Crée un rôle taillé par le vendeur.
/// </summary>
/// <remarks>
/// LES PERMISSIONS ARRIVENT EN CODES, PAS EN ENTIERS.
///
/// `MerchantPermission` est une énumération, et ses valeurs numériques sont un
/// détail d'implémentation : `OFFER_MANAGE` vaut 6 parce qu'elle a été insérée
/// entre `PRODUCT_UNPUBLISH` et le bloc du stock. Accepter l'entier ferait de
/// chaque insertion une rupture de contrat silencieuse — un client qui envoie 6
/// demanderait autre chose après la prochaine livraison, et personne ne le verrait
/// avant qu'un rôle ne porte la mauvaise permission.
///
/// `GET /merchants/permissions` rend déjà les codes ; c'est la même liste qui
/// revient ici.
/// </remarks>
public sealed record CreateSellerRoleCommand(
    Guid SellerId,
    Guid ActorUserId,
    string Name,
    string? Description,
    string? Scope,
    IReadOnlyList<string> Permissions) : ICommand<Guid>;

/// <summary>
/// Réécrit un rôle personnalisé.
/// </summary>
/// <remarks>
/// LES PERMISSIONS SONT REMPLACÉES, PAS FUSIONNÉES — c'est un PUT déguisé en
/// PATCH, et le nom de la route ne doit pas le laisser croire autrement.
///
/// Une fusion obligerait à exprimer les retraits, donc à inventer une grammaire
/// (`-OFFER_PRICE_UPDATE` ?) pour un écran qui, de toute façon, présente des cases
/// à cocher et connaît l'état complet. Le remplacement rend l'appel idempotent et
/// la garde de délégation applicable telle quelle : on vérifie l'ensemble demandé,
/// pas un delta dont il faudrait reconstituer le résultat.
///
/// La PORTÉE, elle, ne se modifie pas — voir le handler.
/// </remarks>
public sealed record UpdateSellerRoleCommand(
    Guid SellerId,
    Guid ActorUserId,
    Guid RoleId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand;

public sealed record DeleteSellerRoleCommand(Guid SellerId, Guid ActorUserId, Guid RoleId) : ICommand;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RÔLES PERSONNALISÉS — CE QUE CE HANDLER GARDE, EN PLUS DE LA PERMISSION.
///
/// `ROLE_CREATE` NE SUFFIT PAS, ET C'EST TOUT L'INTÉRÊT DU §11.
///
/// Un membre qui porte `ROLE_CREATE` pourrait, sans second contrôle, se tailler un
/// rôle portant TOUTES les permissions et se l'attribuer — l'escalade en deux
/// appels. `SellerRole.Custom` exige donc les permissions effectives de l'acteur
/// et refuse tout ce qu'il ne détient pas lui-même. On ne donne pas ce qu'on n'a
/// pas.
///
/// Ce n'est pas une hiérarchie par rang : c'est une inclusion d'ensembles, et elle
/// tient aussi entre deux membres de même niveau aux droits différents — ce qu'un
/// ordinal ne sait pas exprimer.
///
/// ET LE RÔLE CRÉÉ APPARTIENT AU VENDEUR DE L'ACTEUR, PAS À CELUI DU CORPS.
///
/// `SellerId` vient de l'URL, mais c'est la RÉSOLUTION de l'acteur sur ce vendeur
/// qui autorise : un compte qui n'appartient pas à ce vendeur est refusé avant
/// d'arriver ici. Le §36 en toutes lettres.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // Un agrégat ne voit pas ses frères. Le dépôt, si — et une contrainte
        // d'unicité en base rendrait une violation de contrainte que l'appelant
        // recevrait en 500 plutôt qu'en 409 lisible. Les deux se complètent : ce
        // contrôle sert le message, la contrainte sert la concurrence.
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
        //
        // Sinon toute modification de permissions sur un rôle échouerait en 409
        // contre lui-même : `NameExistsAsync` trouverait le rôle qu'on est en train
        // de modifier.
        if (!string.Equals(nom, role.Name, StringComparison.Ordinal)
            && await _roles.NameExistsAsync(command.SellerId, nom, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "sellers.role.name_taken", "Un rôle porte déjà ce nom."));
        }

        // LA PORTÉE NE FIGURE PAS DANS CETTE COMMANDE, ET C'EST DÉLIBÉRÉ.
        //
        // Faire passer un rôle de `Seller` à `Store` — ou l'inverse — changerait le
        // périmètre de tous les membres qui le portent DÉJÀ, sans qu'aucun d'eux ne
        // soit touché ni notifié. C'est une révocation ou une escalade silencieuse
        // selon le sens, et elle serait invisible dans la liste des membres, qui
        // n'affiche que des noms de rôles.
        //
        // Changer de vocation, c'est un autre rôle : on en crée un et on réattribue.
        // ═════════════════════════════════════════════════════════════════════
        // LA DÉCISION D27 SE REJOUE ICI, ET SON ABSENCE ÉTAIT UNE ESCALADE.
        //
        // Le contrôle de portée était posé à l'ATTRIBUTION, jamais à la MODIFICATION.
        // Le détour tenait en trois appels, sans qu'aucun ne soit refusé :
        //
        //   1. créer le rôle « Vendeur B » avec PRODUCT_VIEW et PRODUCT_UPDATE,
        //      toutes deux cloisonnables ;
        //   2. l'affecter à un employé SUR la boutique B — la garde passe, rien
        //      d'incadrable ;
        //   3. y ajouter INVENTORY_ADJUST par un simple PATCH.
        //
        // L'employé se retrouve avec un droit que le code ne sait pas cloisonner,
        // donc appliqué aux DEUX boutiques — exactement ce que D27 interdit, sans
        // qu'aucune attribution n'ait eu lieu. La garde symétrique de
        // `StoreCommandHandler` ne se déclenche pas non plus : elle ne s'exécute
        // qu'à la création d'une boutique.
        //
        // C'est le même raisonnement que celui écrit dans `SellerRole.Update` pour
        // la délégation — « ON REVÉRIFIE À CHAQUE MODIFICATION, PAS SEULEMENT À LA
        // CRÉATION » — appliqué à la seconde règle, qu'on avait oubliée.
        //
        // ET SEULEMENT SI LE RÔLE EST RÉELLEMENT AFFECTÉ À UNE BOUTIQUE.
        //
        // Un rôle porté uniquement au niveau du vendeur ne promet aucun
        // cloisonnement : le contrôle n'a pas lieu d'être, et l'imposer
        // interdirait de modifier des rôles parfaitement légitimes.
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
        //
        // `EnsureDeletable` oppose les trois refus d'un coup : rôle système, rôle
        // encore porté — le supprimer serait une révocation silencieuse, les membres
        // se retrouveraient sans permission, sans événement, sans trace — et rôle
        // portant plus que l'acteur. Les trois vivent dans l'agrégat pour qu'un
        // second chemin de suppression ne puisse pas en oublier un.
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Refuse d'ajouter à un rôle DÉJÀ AFFECTÉ À UNE BOUTIQUE une permission que le
    /// code ne sait pas cloisonner, dès lors que le vendeur a plus d'une boutique.
    /// </summary>
    /// <remarks>
    /// MÊME RÈGLE QUE `MemberCommandHandler` ET `StoreCommandHandler`.
    ///
    /// Les trois gardes forment un triangle : l'une refuse l'affectation, l'autre la
    /// boutique, celle-ci la modification du rôle. En désaccorder une rouvre le trou
    /// par le côté qu'on n'a pas touché — c'est déjà arrivé, cette garde-ci étant la
    /// dernière à avoir été écrite.
    /// </remarks>
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
        //
        // La très grande majorité des modifications ne touchent qu'à des permissions
        // cloisonnables, et n'ont donc rien à payer. L'ordre est une décision de
        // coût : les trois conditions se multiplient, leur ordre ne change rien à la
        // décision.
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
    /// <remarks>
    /// LE RÔLE SYSTÈME ET LE RÔLE D'AUTRUI RENDENT LE MÊME REFUS.
    ///
    /// Un rôle système a `SellerId == null` ; celui d'un concurrent a un autre
    /// `SellerId`. Les deux sont « pas à vous » du point de vue de l'appelant, et
    /// les distinguer dirait à qui tâtonne des identifiants lesquels désignent un
    /// rôle réel. L'agrégat oppose de toute façon son propre refus sur un rôle
    /// système, avec un message explicite, quand l'appelant y arrive légitimement.
    /// </remarks>
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

    /// <summary>
    /// Traduit les codes publics en permissions. Un code inconnu est un REFUS, pas
    /// une omission.
    /// </summary>
    /// <remarks>
    /// IGNORER UN CODE INCONNU CRÉERAIT UN RÔLE PLUS FAIBLE QUE DEMANDÉ.
    ///
    /// L'écran afficherait les cases cochées par l'utilisateur, la base porterait
    /// autre chose, et le premier refus inexpliqué arriverait des semaines plus
    /// tard sur un membre qui « a pourtant le rôle ». Une faute de frappe doit
    /// s'entendre au moment où elle est commise.
    /// </remarks>
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

    /// <summary>
    /// Lit la vocation demandée. Absente, c'est <see cref="RoleScope.Seller"/>.
    /// </summary>
    /// <remarks>
    /// LE DÉFAUT EST `Seller`, ET CE N'EST PAS LE PLUS PERMISSIF PAR HASARD.
    ///
    /// En phase 1, un rôle de vocation `Store` s'applique de toute façon au vendeur
    /// entier — le rattachement boutique ne mord pas encore dans order et inventory
    /// (décision D27). Choisir `Store` par défaut ferait donc croire à un cadrage
    /// qui n'existe pas, ce qui est pire qu'une portée large assumée : on prend des
    /// risques qu'on croit ne pas prendre.
    ///
    /// Le jour où le cadrage mordra, ce défaut devra être rediscuté — pas avant.
    /// </remarks>
    private static Result<RoleScope> LirePortee(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return RoleScope.Seller;
        }

        // `Enum.IsDefined` EN PLUS DE `TryParse`, ET CE N'EST PAS REDONDANT.
        //
        // `Enum.TryParse` accepte les CHAÎNES NUMÉRIQUES sans vérifier qu'elles
        // désignent une valeur déclarée : `"7"` rend `true` et produit
        // `(RoleScope)7`. Le rôle serait persisté avec une valeur hors énumération —
        // `HasConversion<int>` ne s'en plaint pas — et l'écran des rôles afficherait
        // « 7 » dans la colonne portée. Le refus annoncé par cette méthode ne se
        // serait jamais déclenché.
        //
        // Sans conséquence de sécurité aujourd'hui, la vocation ne décidant de rien.
        // Elle en aura le jour où `Scope` pilotera le cadrage.
        return Enum.TryParse<RoleScope>(scope.Trim(), ignoreCase: true, out var portee)
            && Enum.IsDefined(portee)
            ? portee
            : Error.Validation("sellers.role.scope_invalid", $"Portée inconnue : « {scope} ».");
    }
}
