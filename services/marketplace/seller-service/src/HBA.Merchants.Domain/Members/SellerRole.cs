using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Members;

/// <summary>Identité forte d'un rôle vendeur.</summary>
public readonly record struct SellerRoleId(Guid Value)
{
    public static SellerRoleId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Une permission portée par un rôle — une ligne de <c>role_permissions</c>.
/// </summary>
/// <remarks>
/// POURQUOI UNE CLASSE POUR CE QUI POURRAIT ÊTRE UN `List&lt;MerchantPermission&gt;`.
///
/// Parce que le §12 demande une table, et qu'une table sert à quelque chose ici :
/// « combien de membres portent ce rôle » et « quels rôles portent cette
/// permission » sont des questions qu'on posera. Rangées dans une colonne tableau
/// ou en JSON, elles deviendraient des requêtes qu'EF ne traduit pas, ou qu'il
/// traduit mal. C'est le même choix que <c>StaffPermissionOverride</c> côté food.
/// </remarks>
public sealed class SellerRolePermission
{
    private SellerRolePermission()
    {
    }

    internal SellerRolePermission(MerchantPermission permission) => Permission = permission;

    public MerchantPermission Permission { get; private set; }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN RÔLE — SYSTÈME OU PERSONNALISÉ, ET C'EST LE MÊME AGRÉGAT.
///
/// `SellerId` NUL SIGNIFIE « RÔLE SYSTÈME », PARTAGÉ PAR TOUS LES VENDEURS.
///
/// C'est la seule différence structurelle entre les deux, et elle porte toutes
/// les autres : un rôle système ne se modifie pas, ne se supprime pas, et son
/// identifiant est FIXE (voir <see cref="SystemSellerRoles"/>) parce que du code
/// le désigne — <c>SellerMember.IsOwner</c> compare à celui du propriétaire.
///
/// UN RÔLE PERSONNALISÉ NE PEUT PAS PORTER UNE PERMISSION RÉSERVÉE.
///
/// Le §11 dit « restent Owner-only par défaut », ce qui laisse entendre qu'une
/// politique pourrait l'assouplir. Ici c'est absolu : `seller_security_policies`
/// n'existe pas, et une réserve qu'on peut lever par configuration absente n'est
/// pas une réserve. Le jour où la table existera, ce refus sera le seul endroit
/// à rouvrir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerRole : AggregateRoot<SellerRoleId>
{
    private readonly List<SellerRolePermission> _permissions = [];

    private SellerRole()
    {
    }

    private SellerRole(
        SellerRoleId id, Guid? sellerId, string name, string? description,
        RoleScope scope, bool isSystemRole, IEnumerable<MerchantPermission> permissions)
        : base(id)
    {
        SellerId = sellerId;
        Name = name;
        Description = description;
        Scope = scope;
        IsSystemRole = isSystemRole;
        CreatedOnUtc = DateTime.UtcNow;
        _permissions.AddRange(permissions.Distinct().Select(p => new SellerRolePermission(p)));
    }

    /// <summary>Le vendeur propriétaire du rôle, ou <c>null</c> pour un rôle système.</summary>
    public Guid? SellerId { get; private set; }

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    /// <summary>La VOCATION du rôle. En Phase 1, un rôle <see cref="RoleScope.Store"/> s'applique au vendeur.</summary>
    public RoleScope Scope { get; private set; }

    public bool IsSystemRole { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>
    /// Les permissions du rôle.
    /// <para>
    /// RECALCULÉE À CHAQUE APPEL, ET C'EST ASSUMÉ. Un rôle porte quelques
    /// dizaines de valeurs, et l'ensemble effectif d'un membre sera de toute façon
    /// mis en cache par `CheckMerchantCapability` (lot C). Mémoriser ici
    /// introduirait un état à invalider pour un gain nul.
    /// </para>
    /// </summary>
    public IReadOnlySet<MerchantPermission> Permissions
        => _permissions.Select(p => p.Permission).ToHashSet();

    /// <summary>Rôle du propriétaire : le seul qui porte les permissions réservées.</summary>
    public bool IsOwnerRole => Id == SystemSellerRoles.OwnerId;

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>
    /// Un rôle système, à identifiant imposé. Appelé UNIQUEMENT par l'amorçage.
    /// </summary>
    internal static SellerRole System(
        SellerRoleId id, string name, string? description,
        RoleScope scope, IEnumerable<MerchantPermission> permissions)
        => new(id, sellerId: null, name, description, scope, isSystemRole: true, permissions);

    /// <summary>
    /// Un rôle taillé par le vendeur (§18).
    /// </summary>
    /// <param name="acteurPermissions">
    /// Les permissions effectives de celui qui crée le rôle. Le paramètre est
    /// obligatoire, et c'est délibéré : il n'existe aucune surcharge sans acteur,
    /// donc aucun appel non gardé ne compile.
    /// </param>
    public static Result<SellerRole> Custom(
        Guid sellerId,
        string name,
        string? description,
        RoleScope scope,
        IReadOnlySet<MerchantPermission> acteurPermissions,
        IReadOnlyCollection<MerchantPermission> permissions)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("sellers.role.seller_required", "Vendeur manquant.");
        }

        var nom = (name ?? string.Empty).Trim();
        if (nom.Length is 0 or > 100)
        {
            return Error.Validation("sellers.role.name_invalid", "Le nom du rôle doit faire de 1 à 100 caractères.");
        }

        if (permissions.Count == 0)
        {
            return Error.Validation("sellers.role.permissions_required", "Un rôle sans permission ne sert à rien.");
        }

        var refus = EnsureDelegatable(acteurPermissions, permissions);
        if (refus.IsFailure)
        {
            return refus.Error;
        }

        return new SellerRole(
            SellerRoleId.New(), sellerId, nom, description?.Trim(), scope,
            isSystemRole: false, permissions);
    }

    // ── Mutations ───────────────────────────────────────────────────────────

    public Result Update(
        string name,
        string? description,
        IReadOnlyCollection<MerchantPermission> permissions,
        IReadOnlySet<MerchantPermission> acteurPermissions)
    {
        if (IsSystemRole)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.role.system", "Un rôle système ne se modifie pas."));
        }

        var nom = (name ?? string.Empty).Trim();
        if (nom.Length is 0 or > 100)
        {
            return Result.Failure(Error.Validation(
                "sellers.role.name_invalid", "Le nom du rôle doit faire de 1 à 100 caractères."));
        }

        if (permissions.Count == 0)
        {
            return Result.Failure(Error.Validation(
                "sellers.role.permissions_required", "Un rôle sans permission ne sert à rien."));
        }

        // ON REVÉRIFIE À CHAQUE MODIFICATION, PAS SEULEMENT À LA CRÉATION.
        //
        // Sinon un gérant crée un rôle avec ce qu'il a, perd une permission, puis
        // ajoute par modification ce qu'il n'a plus. Le contrôle porte sur l'état
        // de l'acteur AU MOMENT DE L'ÉCRITURE, jamais sur l'historique du rôle.
        var refus = EnsureDelegatable(acteurPermissions, permissions);
        if (refus.IsFailure)
        {
            return refus;
        }

        Name = nom;
        Description = description?.Trim();
        _permissions.Clear();
        _permissions.AddRange(permissions.Distinct().Select(p => new SellerRolePermission(p)));
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Recale les permissions d'un rôle SYSTÈME sur celles du code.
    /// </summary>
    /// <remarks>
    /// APPELÉE UNIQUEMENT PAR L'AMORÇAGE, ET REFUSÉE SUR UN RÔLE PERSONNALISÉ.
    ///
    /// Elle est publique parce que l'amorçage vit dans l'assemblage Infrastructure,
    /// pas parce qu'elle est ouverte à tous : sans acteur ni contrôle de
    /// délégation, l'appliquer à un rôle de vendeur reviendrait à réécrire ses
    /// droits sans que personne ne l'ait décidé. D'où le refus, qui est le seul
    /// garde-fou possible ici.
    /// </remarks>
    public Result SyncSystemPermissions(IReadOnlyCollection<MerchantPermission> permissions)
    {
        if (!IsSystemRole)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.role.not_system",
                "Cette opération ne concerne que les rôles système."));
        }

        _permissions.Clear();
        _permissions.AddRange(permissions.Distinct().Select(p => new SellerRolePermission(p)));
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>
    /// Un rôle système ne se supprime pas ; un rôle encore attribué non plus ; et un
    /// rôle portant plus que l'acteur non plus.
    /// </summary>
    /// <param name="acteurPermissions">
    /// Les permissions effectives de celui qui supprime.
    /// </param>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA DÉLÉGATION VAUT AUSSI POUR LA SUPPRESSION, ET CE N'EST PAS ÉVIDENT.
    ///
    /// `ROLE_DELETE` seul suffirait à effacer un rôle portant des permissions que
    /// l'acteur ne détient pas. Ce n'est pas une escalade — on ne gagne rien à
    /// supprimer — mais c'est un DÉGÂT que le §11 est censé borner : un
    /// gestionnaire de catalogue pourrait effacer le rôle du comptable, et le rôle
    /// ne se recrée qu'en le retapant permission par permission.
    ///
    /// La règle est donc symétrique à la création : l'autorité d'un acteur sur un
    /// rôle se mesure aux permissions que ce rôle porte, jamais au verbe HTTP.
    ///
    /// ET ELLE VIT ICI PLUTÔT QUE DANS LE HANDLER.
    ///
    /// Elle y était d'abord. Le handler est aujourd'hui le seul appelant — mais
    /// `EnsureDeletable` est publique, et un second chemin de suppression (reprise
    /// de données, commande d'administration) recopierait les deux premiers refus
    /// en oubliant le troisième. Les trois raisons de refuser une suppression
    /// tiennent dans une seule méthode, ou aucune ne tient.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Result EnsureDeletable(
        int membresPortantCeRole, IReadOnlySet<MerchantPermission> acteurPermissions)
    {
        var delegation = EnsureDeletable(membresPortantCeRole);
        if (delegation.IsFailure)
        {
            return delegation;
        }

        // `Cast<MerchantPermission?>` AVANT `FirstOrDefault` : sans lui, le défaut
        // d'une énumération est `0`, c'est-à-dire `PRODUCT_VIEW`. Le test « aucune
        // permission hors portée » deviendrait « la première hors portée est-elle
        // PRODUCT_VIEW », et le refus tomberait exactement à l'envers.
        var horsPortee = Permissions
            .Where(p => !acteurPermissions.Contains(p))
            .Cast<MerchantPermission?>()
            .FirstOrDefault();

        return horsPortee is { } interdite
            ? Result.Failure(Error.Forbidden(
                "sellers.role.cannot_delegate",
                $"Ce rôle porte « {interdite.ToCode()} », dont vous ne disposez pas vous-même."))
            : Result.Success();
    }

    /// <summary>Un rôle système ne se supprime pas ; un rôle encore attribué non plus.</summary>
    public Result EnsureDeletable(int membresPortantCeRole)
    {
        if (IsSystemRole)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.role.system", "Un rôle système ne se supprime pas."));
        }

        // SUPPRIMER UN RÔLE ATTRIBUÉ EST UNE RÉVOCATION SILENCIEUSE.
        //
        // Les membres qui le portaient se retrouveraient sans permission, sans
        // événement, sans trace — et le propriétaire chercherait la panne du côté
        // des comptes. On exige de détacher d'abord.
        if (membresPortantCeRole > 0)
        {
            return Result.Failure(Error.Conflict(
                "sellers.role.in_use",
                $"Ce rôle est encore porté par {membresPortantCeRole} membre(s)."));
        }

        return Result.Success();
    }

    public bool Has(MerchantPermission permission) => _permissions.Any(p => p.Permission == permission);

    // ── La règle anti-escalade (§11 et §36) ─────────────────────────────────

    /// <summary>
    /// ON NE DONNE PAS CE QU'ON N'A PAS, ET JAMAIS CE QUI EST RÉSERVÉ.
    /// </summary>
    /// <remarks>
    /// C'est ce qui remplace, ici, la hiérarchie par ordinal de
    /// <c>RestaurantStaff</c>. Un rang dit « je suis plus haut que toi » ; une
    /// inclusion d'ensembles dit « je ne peux pas donner ce que je n'ai pas ».
    /// La seconde est plus forte : elle tient aussi entre deux membres de même
    /// rang aux permissions différentes, ce qu'un ordinal ne sait pas exprimer.
    /// </remarks>
    private static Result EnsureDelegatable(
        IReadOnlySet<MerchantPermission> acteurPermissions,
        IReadOnlyCollection<MerchantPermission> demandees)
    {
        var reservee = demandees
            .Where(p => p.IsOwnerOnly())
            .Cast<MerchantPermission?>()
            .FirstOrDefault();

        if (reservee is { } interdite)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.role.owner_only",
                $"« {interdite.ToCode()} » est réservée au propriétaire et ne peut être portée par un rôle."));
        }

        var manquante = demandees
            .Where(p => !acteurPermissions.Contains(p))
            .Cast<MerchantPermission?>()
            .FirstOrDefault();

        if (manquante is { } absente)
        {
            return Result.Failure(Error.Forbidden(
                "sellers.role.cannot_delegate",
                $"Vous ne disposez pas vous-même de « {absente.ToCode()} »."));
        }

        return Result.Success();
    }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES RÔLES SYSTÈME — IDENTIFIANTS FIXES, ET POURQUOI ILS LE SONT.
///
/// CES GUID SONT ÉCRITS EN BASE, DANS `seller_member_roles`. ILS NE CHANGENT
///    JAMAIS.
///
/// Un rôle système semé avec un identifiant aléatoire obligerait chaque lecture à
/// le retrouver par son nom — et un renommage casserait tout. Ici l'amorçage est
/// idempotent parce que l'identité est connue d'avance, et `IsOwner` est une
/// comparaison, pas une requête.
///
/// LA COLONNE `Scope` EST LA VOCATION, PAS L'APPLICATION (décision D27).
///
/// Les cinq rôles marqués `Store` s'appliquent aujourd'hui au VENDEUR ENTIER,
/// faute de rattachement boutique dans order et inventory. C'est sans effet chez
/// un vendeur mono-boutique — tous le sont aujourd'hui, la migration de reprise
/// en a créé exactement une par vendeur — et c'est une escalade dès la deuxième.
/// D'où le refus d'attribution posé dans la couche Application.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class SystemSellerRoles
{
    public static readonly SellerRoleId OwnerId = new(Guid.Parse("a5100001-0000-4000-8000-000000000001"));
    public static readonly SellerRoleId SellerAdminId = new(Guid.Parse("a5100002-0000-4000-8000-000000000002"));
    public static readonly SellerRoleId StoreAdminId = new(Guid.Parse("a5100003-0000-4000-8000-000000000003"));
    public static readonly SellerRoleId CatalogManagerId = new(Guid.Parse("a5100004-0000-4000-8000-000000000004"));
    public static readonly SellerRoleId InventoryManagerId = new(Guid.Parse("a5100005-0000-4000-8000-000000000005"));
    public static readonly SellerRoleId OrderManagerId = new(Guid.Parse("a5100006-0000-4000-8000-000000000006"));
    public static readonly SellerRoleId CustomerSupportId = new(Guid.Parse("a5100007-0000-4000-8000-000000000007"));
    public static readonly SellerRoleId FinanceManagerId = new(Guid.Parse("a5100008-0000-4000-8000-000000000008"));
    public static readonly SellerRoleId EmployeeId = new(Guid.Parse("a5100009-0000-4000-8000-000000000009"));

    /// <summary>Le catalogue tel qu'il est semé. Ordre d'affichage compris.</summary>
    public static IReadOnlyList<SellerRole> Catalogue { get; } =
    [
        // LE PROPRIÉTAIRE PORTE TOUT, Y COMPRIS LES PERMISSIONS RÉSERVÉES.
        // Ce n'est pas une délégation : c'est son propre dossier.
        SellerRole.System(
            OwnerId, "OWNER", "Propriétaire du dossier vendeur. Contrôle complet.",
            RoleScope.Seller, MerchantPermissions.All),

        // TOUT SAUF LE RÉSERVÉ — c'est exactement la définition du §10
        // (« administration générale hors actions réservées Owner »).
        SellerRole.System(
            SellerAdminId, "SELLER_ADMIN", "Administration générale, hors actions réservées au propriétaire.",
            RoleScope.Seller, MerchantPermissions.All.Where(p => !p.IsOwnerOnly())),

        SellerRole.System(
            StoreAdminId, "STORE_ADMIN", "Administration complète d'une boutique.",
            RoleScope.Store,
            [
                MerchantPermission.ProductView, MerchantPermission.ProductCreate,
                MerchantPermission.ProductUpdate, MerchantPermission.ProductSubmitForReview,
                MerchantPermission.ProductPublish, MerchantPermission.ProductUnpublish,
                MerchantPermission.OfferManage, MerchantPermission.OfferPriceUpdate,
                MerchantPermission.InventoryView, MerchantPermission.InventoryAdjust,
                MerchantPermission.InventoryTransfer, MerchantPermission.StockMovementView,
                MerchantPermission.StockLocationView, MerchantPermission.StockLocationManage,
                MerchantPermission.OrderView, MerchantPermission.OrderConfirm,
                MerchantPermission.OrderReject, MerchantPermission.OrderMarkPreparing,
                MerchantPermission.OrderMarkReady, MerchantPermission.OrderCancel,
                MerchantPermission.ReviewView, MerchantPermission.ReviewReply,
                MerchantPermission.ReturnView, MerchantPermission.ReturnApprove,
                MerchantPermission.ReturnReject, MerchantPermission.ReturnConfirmReceived,
                MerchantPermission.ReturnInspect, MerchantPermission.ReturnDisputeView,
                MerchantPermission.StoreView, MerchantPermission.StoreUpdate,
                MerchantPermission.StoreOpenClose,
                MerchantPermission.MemberView
            ]),

        SellerRole.System(
            CatalogManagerId, "CATALOG_MANAGER", "Produits, déclinaisons, soumission à validation.",
            RoleScope.Store,
            [
                MerchantPermission.ProductView, MerchantPermission.ProductCreate,
                MerchantPermission.ProductUpdate, MerchantPermission.ProductSubmitForReview,
                MerchantPermission.ProductPublish, MerchantPermission.ProductUnpublish,

                // LE PRIX AVEC LE RESTE, POUR CE RÔLE ET LUI SEUL PARMI LES
                // MÉTIERS. « Gestionnaire de catalogue » sans le droit de fixer un
                // prix ne peut pas mettre un produit en vente — il s'arrêterait à
                // la fiche et attendrait le patron pour la dernière ligne.
                MerchantPermission.OfferManage, MerchantPermission.OfferPriceUpdate,
                MerchantPermission.StoreView
            ]),

        SellerRole.System(
            InventoryManagerId, "INVENTORY_MANAGER", "Stocks, ajustements, transferts.",
            RoleScope.Store,
            [
                MerchantPermission.InventoryView, MerchantPermission.InventoryAdjust,
                MerchantPermission.InventoryTransfer, MerchantPermission.StockMovementView,
                MerchantPermission.StockLocationView, MerchantPermission.StockLocationManage,
                MerchantPermission.ProductView, MerchantPermission.StoreView
            ]),

        // NI `INVENTORY_ADJUST`, NI `MEMBER_*` — c'est le test §24 qui le dit :
        // « Order Manager peut confirmer une commande mais pas ajuster le stock ».
        SellerRole.System(
            OrderManagerId, "ORDER_MANAGER", "Commandes et préparation.",
            RoleScope.Store,
            [
                MerchantPermission.OrderView, MerchantPermission.OrderConfirm,
                MerchantPermission.OrderReject, MerchantPermission.OrderMarkPreparing,
                MerchantPermission.OrderMarkReady,
                MerchantPermission.ProductView, MerchantPermission.InventoryView,
                MerchantPermission.StockLocationView,
                MerchantPermission.ReviewView, MerchantPermission.ReturnView,
                MerchantPermission.StoreView
            ]),

        SellerRole.System(
            CustomerSupportId, "CUSTOMER_SUPPORT", "Lecture des commandes, réponses aux avis, suivi des retours.",
            RoleScope.Store,
            [
                MerchantPermission.OrderView,
                MerchantPermission.ProductView,
                MerchantPermission.ReviewView, MerchantPermission.ReviewReply,
                MerchantPermission.ReturnView, MerchantPermission.ReturnDisputeView,
                MerchantPermission.StoreView
            ]),

        // PAS DE `WITHDRAWAL_REQUEST` PAR DÉFAUT, ET C'EST UN CHOIX.
        //
        // Le cahier l'autorise à ce rôle. Un comptable lit un relevé ; demander un
        // virement vers un compte qu'il ne contrôle pas est un autre métier. Les
        // défauts d'un rôle sont un plancher sûr : le propriétaire peut l'ajouter
        // explicitement, et l'ajout laissera une trace que l'implicite n'aurait
        // pas laissée.
        SellerRole.System(
            FinanceManagerId, "FINANCE_MANAGER", "Lecture des finances et des règlements.",
            RoleScope.Seller,
            [
                MerchantPermission.FinanceView, MerchantPermission.WalletView,
                MerchantPermission.PayoutView, MerchantPermission.OrderView,
                MerchantPermission.AuditView
            ]),

        SellerRole.System(
            EmployeeId, "EMPLOYEE", "Rôle minimal, destiné à être personnalisé.",
            RoleScope.Store,
            [
                MerchantPermission.OrderView,
                MerchantPermission.ProductView,
                MerchantPermission.InventoryView
            ])
    ];
}

/// <summary>Accès aux rôles. L'interface vit dans le fichier de l'agrégat, comme <c>IStoreRepository</c>.</summary>
public interface ISellerRoleRepository
{
    Task<SellerRole?> GetByIdAsync(SellerRoleId id, CancellationToken cancellationToken = default);

    /// <summary>Les rôles système ET ceux du vendeur — l'ensemble de ce qu'il peut attribuer.</summary>
    Task<IReadOnlyList<SellerRole>> ListAvailableAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>Résout un lot d'identifiants ; les inconnus sont simplement absents du résultat.</summary>
    Task<IReadOnlyList<SellerRole>> ListByIdsAsync(
        IReadOnlyCollection<SellerRoleId> ids, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(Guid sellerId, string name, CancellationToken cancellationToken = default);

    Task AddAsync(SellerRole role, CancellationToken cancellationToken = default);

    void Remove(SellerRole role);
}
