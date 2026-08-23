using HBA.Inventory.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Domain.Stores;

namespace HBA.Merchants.Application.Stores;

/// <summary>
/// Crée une boutique pour un vendeur.
///
/// C'EST ICI QUE LE MULTI-BOUTIQUES DEVIENT RÉEL. Rien ne limite le nombre de
/// boutiques d'un vendeur — c'est la promesse du cahier, et l'unique raison pour
/// laquelle Store est un agrégat distinct de Seller.
/// </summary>
public sealed record CreateStoreCommand(
    Guid SellerId, string Name, string ContactPhone, string? ContactEmail) : ICommand<Guid>;

public sealed record UpdateStoreProfileCommand(
    Guid StoreId, Guid SellerId, string Name, string? LogoUrl, string? Description) : ICommand;

public sealed record UpdateStoreContactCommand(
    Guid StoreId, Guid SellerId, string ContactPhone, string? ContactEmail) : ICommand;

/// <summary>Rattache le lieu d'où partent les colis de cette boutique.</summary>
public sealed record AttachStoreLocationCommand(
    Guid StoreId, Guid SellerId, Guid FulfillmentLocationId) : ICommand;

/// <summary>Un créneau, en entrée. Les heures sont au format « HH:mm ».</summary>
public sealed record OpeningHourInput(string Day, string OpensAt, string ClosesAt);

/// <summary>Remplace la grille horaire — voir Store.SetOpeningHours.</summary>
public sealed record SetStoreOpeningHoursCommand(
    Guid StoreId, Guid SellerId, IReadOnlyList<OpeningHourInput> Hours) : ICommand;

public sealed record OpenStoreCommand(Guid StoreId, Guid SellerId) : ICommand;

public sealed record CloseStoreCommand(Guid StoreId, Guid SellerId, string? Reason) : ICommand;

/// <summary>Suspension d'une boutique par la PLATEFORME. Pas de SellerId : c'est une décision d'admin.</summary>
public sealed record SuspendStoreCommand(Guid StoreId, string? Reason) : ICommand;

/// <summary>Levée de suspension (Admin). La boutique repasse en « fermée », pas en « ouverte ».</summary>
public sealed record LiftStoreSuspensionCommand(Guid StoreId) : ICommand;

internal sealed class StoreCommandHandler
    : ICommandHandler<CreateStoreCommand, Guid>,
      ICommandHandler<UpdateStoreProfileCommand>,
      ICommandHandler<UpdateStoreContactCommand>,
      ICommandHandler<AttachStoreLocationCommand>,
      ICommandHandler<SetStoreOpeningHoursCommand>,
      ICommandHandler<OpenStoreCommand>,
      ICommandHandler<CloseStoreCommand>,
      ICommandHandler<SuspendStoreCommand>,
      ICommandHandler<LiftStoreSuspensionCommand>
{
    private readonly IStoreRepository _stores;
    private readonly ISellerRepository _sellers;
    private readonly ISellerMemberRepository _members;
    private readonly ISellerRoleRepository _roles;
    private readonly IInventoryModuleApi _inventory;
    private readonly ISellerUnitOfWork _unitOfWork;

    public StoreCommandHandler(
        IStoreRepository stores,
        ISellerRepository sellers,
        ISellerMemberRepository members,
        ISellerRoleRepository roles,
        IInventoryModuleApi inventory,
        ISellerUnitOfWork unitOfWork)
    {
        _stores = stores;
        _sellers = sellers;
        _members = members;
        _roles = roles;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateStoreCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellers.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure<Guid>(Error.NotFound("sellers.seller.not_found", "Vendeur introuvable."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // ON REFUSE À UN VENDEUR **SANCTIONNÉ OU FERMÉ** D'OUVRIR UNE BOUTIQUE.
        //
        // Un vendeur suspendu ou fermé qui pourrait créer une boutique neuve
        // contournerait sa sanction : la nouvelle n'aurait pas été retirée de la
        // vente, puisqu'elle n'existait pas au moment du retrait.
        //
        // CETTE GARDE A ÉTÉ ÉCRITE `!= Active`, ET C'ÉTAIT TROP LARGE.
        //
        // Elle refusait aussi à un vendeur `Pending` — celui qui vient de
        // s'inscrire et n'a pas encore franchi le KYB — d'ouvrir sa PREMIÈRE
        // boutique. Or c'est l'ordre normal du parcours : on s'inscrit, on monte sa
        // boutique, puis on dépose son dossier. Le refus arrivait en 409
        // « Seul un vendeur actif peut ouvrir une boutique », c'est-à-dire une
        // impasse pour tout nouveau vendeur.
        //
        // La liste est donc explicite plutôt que complémentaire. `PendingReactivation`
        // en fait partie : c'est un compte fermé qui attend une validation, il n'a
        // pas plus le droit d'ouvrir une boutique que lorsqu'il était `Closed`.
        // ═════════════════════════════════════════════════════════════════════
        if (seller.Status is SellerStatus.Suspended or SellerStatus.Closed or SellerStatus.PendingReactivation)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "sellers.store.seller_not_active",
                "Un vendeur suspendu ou fermé ne peut pas ouvrir de boutique."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE PENDANT DE LA DÉCISION D27 — ET IL VA PAR PAIRE AVEC L'AUTRE.
        //
        // Un rôle à vocation boutique (ORDER_MANAGER, INVENTORY_MANAGER…) donne
        // aujourd'hui ses permissions sur le VENDEUR ENTIER : ni `OrderLine` ni
        // `InventoryItem` ne connaît la boutique. Tant que le vendeur n'en a
        // qu'une, la distinction n'existe pas.
        //
        // L'attribution de ces rôles est déjà refusée au-delà d'une boutique.
        // Sans cette garde-ci, il suffirait d'inverser l'ordre — recruter un
        // gestionnaire de commandes, PUIS ouvrir la seconde boutique — pour
        // obtenir exactement l'escalade que l'autre interdit. Une garde seule
        // ferme une porte et laisse l'autre.
        //
        // Elle disparaîtra au lot G, quand `Enforcement` passera à `Enforced`.
        // ═════════════════════════════════════════════════════════════════════
        var portee = await EnsurePorteeBoutiqueAsync(command.SellerId, cancellationToken);
        if (portee.IsFailure)
        {
            return Result.Failure<Guid>(portee.Error);
        }

        var contact = BusinessContact.Create(command.ContactPhone, command.ContactEmail);
        if (contact.IsFailure)
        {
            return Result.Failure<Guid>(contact.Error);
        }

        var store = Store.Create(command.SellerId, command.Name, contact.Value);
        if (store.IsFailure)
        {
            return Result.Failure<Guid>(store.Error);
        }

        await _stores.AddAsync(store.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return store.Value.Id.Value;
    }

    public Task<Result> Handle(UpdateStoreProfileCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, command.SellerId, cancellationToken,
            store => store.UpdateProfile(command.Name, command.LogoUrl, command.Description));

    public Task<Result> Handle(UpdateStoreContactCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, command.SellerId, cancellationToken, store =>
        {
            var contact = BusinessContact.Create(command.ContactPhone, command.ContactEmail);
            return contact.IsFailure ? Result.Failure(contact.Error) : store.UpdateContact(contact.Value);
        });

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RATTACHE LE LIEU D'EXPÉDITION — APRÈS AVOIR VÉRIFIÉ QU'IL EST BIEN À CE VENDEUR.
    ///
    /// CE CONTRÔLE ÉTAIT DÉLÉGUÉ À PERSONNE.
    ///
    /// Ce handler portait : « L'appartenance du lieu au vendeur n'est pas vérifiée
    /// ici. […] Le contrôle est fait par l'appelant, qui voit les deux modules —
    /// voir la route du BFF Vendeur. » Le BFF Vendeur annonce lui-même n'exposer
    /// aucun cas d'usage. Comme pour la pièce KYB (D22), une délégation vers un
    /// destinataire inexistant se lisait comme une décision d'architecture.
    ///
    /// N'importe quel GUID passait : le `SellerAddress` d'un concurrent, un
    /// `PlatformWarehouse`, ou un identifiant qui n'existe pas. `Store.Open()`
    /// acceptait ensuite la boutique, et l'identifiant partait vers delivery, qui
    /// bâtissait un enlèvement coursier sur une adresse que le vendeur ne contrôle
    /// pas. Le GUID inexistant, lui, ne se manifestait qu'APRÈS le paiement de
    /// l'acheteur, sur la jambe coursier — au moment le plus cher du parcours.
    ///
    /// ET « SELLERS NE CONNAÎT PAS INVENTORY » N'ÉTAIT PAS UN ARGUMENT.
    ///
    /// `IInventoryModuleApi.GetLocationAsync` existe et transporte `OwnerId` — le
    /// contrat était là, il n'était simplement pas appelé. Dépendre d'un contrat
    /// pour valider une entrée qu'on va persister n'est pas une fuite de couche :
    /// c'est ce que fait déjà `RegisterSeller` avec Identity, et `AddKybDocument`
    /// avec Media.
    ///
    /// UN ENTREPÔT PLATEFORME EST REFUSÉ, ET C'EST DÉLIBÉRÉ.
    ///
    /// Son `OwnerId` est nul par construction (FBP). Le laisser passer rendrait la
    /// garde inopérante — n'importe quel vendeur pointerait n'importe quel
    /// entrepôt. Confier une boutique à un entrepôt de la plateforme est une
    /// décision d'EXPLOITATION, pas un geste de vendeur ; le jour où elle sera
    /// nécessaire, elle méritera sa propre route d'administration, nommée pour ce
    /// qu'elle fait.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<Result> Handle(AttachStoreLocationCommand command, CancellationToken cancellationToken)
    {
        var lieu = await _inventory.GetLocationAsync(command.FulfillmentLocationId, cancellationToken);

        if (lieu is null)
        {
            return Result.Failure(Error.NotFound(
                "sellers.store.location_not_found",
                "Ce lieu d'expédition n'existe pas. Créez-le avant de le rattacher à une boutique."));
        }

        // 403 et non 404 : le lieu EXISTE. Dire « introuvable » ici rendrait ce
        // refus indiscernable du précédent, et un vendeur qui s'est trompé de
        // lieu — le sien, mais celui d'une autre boutique — ne saurait pas lequel
        // des deux problèmes il a.
        if (lieu.OwnerId != command.SellerId)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.store.location_not_owned",
                "Ce lieu d'expédition n'appartient pas à ce vendeur."));
        }

        return await MutateAsync(command.StoreId, command.SellerId, cancellationToken,
            store => store.AttachFulfillmentLocation(command.FulfillmentLocationId));
    }

    public Task<Result> Handle(SetStoreOpeningHoursCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, command.SellerId, cancellationToken, store =>
        {
            var creneaux = new List<StoreOpeningHour>();

            foreach (var entree in command.Hours)
            {
                if (!Enum.TryParse<DayOfWeek>(entree.Day, ignoreCase: true, out var jour))
                {
                    return Result.Failure(Error.Validation(
                        "sellers.store.day_invalid", $"Jour invalide : « {entree.Day} »."));
                }

                // CULTURE INVARIANTE. Le projet tourne en InvariantGlobalization ;
                // s'en remettre à la culture courante ferait dépendre l'analyse d'un
                // réglage serveur, et « 14:30 » cesserait d'être lu un jour sans
                // qu'aucun code n'ait changé.
                if (!TimeOnly.TryParse(entree.OpensAt, System.Globalization.CultureInfo.InvariantCulture, out var ouverture)
                    || !TimeOnly.TryParse(entree.ClosesAt, System.Globalization.CultureInfo.InvariantCulture, out var fermeture))
                {
                    return Result.Failure(Error.Validation(
                        "sellers.store.hours_invalid", "Heures attendues au format « HH:mm »."));
                }

                var creneau = StoreOpeningHour.Create(jour, ouverture, fermeture);
                if (creneau.IsFailure)
                {
                    return Result.Failure(creneau.Error);
                }

                creneaux.Add(creneau.Value);
            }

            return store.SetOpeningHours(creneaux);
        });

    public Task<Result> Handle(OpenStoreCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, command.SellerId, cancellationToken, store => store.Open());

    public Task<Result> Handle(CloseStoreCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, command.SellerId, cancellationToken, store => store.Close(command.Reason));

    // Les deux décisions d'ADMIN passent par le chemin sans contrôle de
    // propriété : l'exploitation agit sur la boutique d'autrui, c'est son rôle.
    public Task<Result> Handle(SuspendStoreCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, ownerSellerId: null, cancellationToken,
            store => store.Suspend(command.Reason));

    public Task<Result> Handle(LiftStoreSuspensionCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.StoreId, ownerSellerId: null, cancellationToken,
            store => store.LiftSuspension());

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// REFUSE UNE BOUTIQUE DE PLUS TANT QU'UNE AFFECTATION PORTE UN DROIT
    /// INCADRABLE (décision D27, resserrée au lot G).
    ///
    /// LA RÈGLE PORTE SUR LES RÔLES DE BOUTIQUE, PAS SUR CEUX DU VENDEUR.
    ///
    /// C'est le correctif d'une régression que le lot G a introduite. La version
    /// précédente collectait `ReferencedRoleIds` de TOUS les membres actifs — donc
    /// aussi les rôles attribués au niveau du vendeur — et refusait dès qu'une
    /// permission n'était pas cloisonnable. Or le PROPRIÉTAIRE porte OWNER, qui
    /// porte `MerchantPermissions.All`, dont l'immense majorité n'est pas
    /// cloisonnable. Conséquence : plus aucun vendeur ne pouvait ouvrir sa
    /// deuxième boutique, avec un message lui demandant de retirer une permission
    /// d'un rôle système qu'il n'a pas le droit de modifier. La multi-boutique
    /// était morte.
    ///
    /// Le commentaire d'origine le disait pourtant — « le propriétaire n'entre
    /// jamais dans ce décompte, sans quoi plus aucun vendeur ne pourrait ouvrir sa
    /// deuxième boutique » — mais il décrivait le filtre PRÉCÉDENT, sur
    /// `RoleScope`. Le refactor n'a pas été propagé au texte, et le texte a cessé
    /// d'être vrai sans que rien ne le signale.
    ///
    /// ET LA DISTINCTION N'EST PAS COSMÉTIQUE : ELLE EST LA RÈGLE.
    ///
    /// Un droit donné AU NIVEAU DU VENDEUR est un choix explicite : le vendeur a
    /// voulu que ce comptable voie les finances de toute l'entreprise. Il n'y a
    /// rien à protéger. Un droit donné VIA UNE AFFECTATION BOUTIQUE porte une
    /// promesse de cloisonnement — « tu t'occupes du magasin B » — et c'est cette
    /// promesse-là que le code ne sait pas encore tenir pour `INVENTORY_*` et
    /// `ORDER_*`. C'est elle, et elle seule, qu'on refuse de faire à moitié.
    ///
    /// LE PENDANT VIT DANS `MemberCommandHandler`, ET SUIT LA MÊME RÈGLE.
    ///
    /// Les deux gardes vont par paire : celle-ci refuse la boutique quand une
    /// affectation la rendrait ambiguë, l'autre refuse l'affectation quand la
    /// boutique existe déjà. Les désaccorder rouvre le trou par le bout qu'on n'a
    /// pas touché.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<Result> EnsurePorteeBoutiqueAsync(
        Guid sellerId, CancellationToken cancellationToken)
    {
        var existantes = await _stores.ListBySellerAsync(sellerId, cancellationToken);
        if (existantes.Count == 0)
        {
            return Result.Success();
        }

        var membres = await _members.ListBySellerAsync(sellerId, cancellationToken);

        // `StoreMemberships` ET NON `ReferencedRoleIds`.
        //
        // La seconde réunit les rôles vendeur ET les rôles de boutique. C'est
        // précisément la confusion qui bloquait le propriétaire : ses rôles sont au
        // niveau du vendeur, il n'a aucune affectation, et il n'a donc rien à voir
        // avec cette question.
        var rolesAffectes = membres
            .Where(m => m.CanAct)
            .SelectMany(m => m.StoreMemberships.Where(a => a.Status == StoreMembershipStatus.Active))
            .SelectMany(a => a.RoleIds)
            .Distinct()
            .ToArray();

        if (rolesAffectes.Length == 0)
        {
            return Result.Success();
        }

        var roles = await _roles.ListByIdsAsync(rolesAffectes, cancellationToken);

        var incadrable = roles
            .SelectMany(r => r.Permissions)
            .Where(p => !p.IsStoreScoped())
            .Cast<MerchantPermission?>()
            .FirstOrDefault();

        return incadrable is { } bloquante
            ? Result.Failure(Error.Conflict(
                "sellers.store.member_scope_conflict",
                $"Un membre affecté à une boutique porte « {bloquante.ToCode()} », qui ne peut pas encore "
                + "être cloisonnée : ouvrir une seconde boutique lui donnerait accès aux deux. Retirez "
                + "cette permission de son rôle de boutique, ou attribuez-la au niveau du vendeur si "
                + "c'est bien ce que vous voulez."))
            : Result.Success();
    }

    /// <summary>
    /// Charge, vérifie la propriété, applique, enregistre.
    ///
    /// LE CONTRÔLE DE PROPRIÉTÉ EST ICI, ET NULLE PART AILLEURS.
    ///
    /// Une boutique est désignée par un GUID que l'appelant fournit. Sans ce
    /// contrôle, un vendeur authentifié fermerait la boutique d'un concurrent en
    /// changeant un identifiant dans une URL — et un identifiant de boutique
    /// circule dans les liens publics.
    ///
    /// On répond « introuvable » et non « interdit » : distinguer les deux dirait
    /// à qui teste des identifiants lesquels existent.
    /// </summary>
    private async Task<Result> MutateAsync(
        Guid storeId, Guid? ownerSellerId, CancellationToken cancellationToken, Func<Store, Result> action)
    {
        var store = await _stores.GetByIdAsync(new StoreId(storeId), cancellationToken);

        if (store is null || (ownerSellerId is { } sellerId && store.SellerId != sellerId))
        {
            return Result.Failure(Error.NotFound("sellers.store.not_found", "Boutique introuvable."));
        }

        var result = action(store);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
