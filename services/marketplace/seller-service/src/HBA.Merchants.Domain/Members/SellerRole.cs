using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Merchants.Domain.Members;

/// <summary>Identité forte d'un rôle vendeur.</summary>
public readonly record struct SellerRoleId(Guid Value)
{
    public static SellerRoleId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Une permission portée par un rôle — une ligne de <c>role_permissions</c>.</summary>
public sealed class SellerRolePermission
{
    private SellerRolePermission()
    {
    }

    internal SellerRolePermission(MerchantPermission permission) => Permission = permission;

    public MerchantPermission Permission { get; private set; }
}

/// <summary>UN RÔLE — SYSTÈME OU PERSONNALISÉ, ET C'EST LE MÊME AGRÉGAT.</summary>
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

    /// <summary>
    /// La VOCATION du rôle. En Phase 1, un rôle <see cref="RoleScope.Store"/>
    /// s'applique au vendeur.
    /// </summary>
    public RoleScope Scope { get; private set; }

    public bool IsSystemRole { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Les permissions du rôle.</summary>
    public IReadOnlySet<MerchantPermission> Permissions
        => _permissions.Select(p => p.Permission).ToHashSet();

    /// <summary>Rôle du propriétaire : le seul qui porte les permissions réservées.</summary>
    public bool IsOwnerRole => Id == SystemSellerRoles.OwnerId;

    // ── Création ────────────────────────────────────────────────────────────

    /// <summary>Un rôle système, à identifiant imposé.</summary>
    internal static SellerRole System(
        SellerRoleId id, string name, string? description,
        RoleScope scope, IEnumerable<MerchantPermission> permissions)
        => new(id, sellerId: null, name, description, scope, isSystemRole: true, permissions);

    /// <summary>Un rôle taillé par le vendeur (§18).</summary>
    /// <param name="acteurPermissions">
    /// Les permissions effectives de celui qui crée le rôle.
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

    /// <summary>Recale les permissions d'un rôle SYSTÈME sur celles du code.</summary>
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
    /// Un rôle système ne se supprime pas ; un rôle encore attribué non plus ; et
    /// un rôle portant plus que l'acteur non plus.
    /// </summary>
    /// <param name="acteurPermissions">Les permissions effectives de celui qui supprime.</param>
    public Result EnsureDeletable(
        int membresPortantCeRole, IReadOnlySet<MerchantPermission> acteurPermissions)
    {
        var delegation = EnsureDeletable(membresPortantCeRole);
        if (delegation.IsFailure)
        {
            return delegation;
        }

        // `Cast<MerchantPermission?>` AVANT `FirstOrDefault` : sans lui, le défaut
        // d'une énumération est `0`, c'est-à-dire `PRODUCT_VIEW`.
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

    /// <summary>ON NE DONNE PAS CE QU'ON N'A PAS, ET JAMAIS CE QUI EST RÉSERVÉ.</summary>
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

/// <summary>LES RÔLES SYSTÈME — IDENTIFIANTS FIXES, ET POURQUOI ILS LE SONT.</summary>
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

    /// <summary>Le catalogue tel qu'il est semé.</summary>
    public static IReadOnlyList<SellerRole> Catalogue { get; } =
    [
        // LE PROPRIÉTAIRE PORTE TOUT, Y COMPRIS LES PERMISSIONS RÉSERVÉES. Ce n'est
        // pas une délégation : c'est son propre dossier.
        SellerRole.System(
            OwnerId, "OWNER", "Propriétaire du dossier vendeur. Contrôle complet.",
            RoleScope.Seller, MerchantPermissions.All),

        // TOUT SAUF LE RÉSERVÉ — c'est exactement la définition du §10 («
        // administration générale hors actions réservées Owner »).
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

        // NI `INVENTORY_ADJUST`, NI `MEMBER_*` — c'est le test §24 qui le dit : «
        // Order Manager peut confirmer une commande mais pas ajuster le stock ».
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

/// <summary>
/// Accès aux rôles. L'interface vit dans le fichier de l'agrégat, comme <c>
/// IStoreRepository</c>.
/// </summary>
public interface ISellerRoleRepository
{
    Task<SellerRole?> GetByIdAsync(SellerRoleId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Les rôles système ET ceux du vendeur — l'ensemble de ce qu'il peut
    /// attribuer.
    /// </summary>
    Task<IReadOnlyList<SellerRole>> ListAvailableAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Résout un lot d'identifiants ; les inconnus sont simplement absents du
    /// résultat.
    /// </summary>
    Task<IReadOnlyList<SellerRole>> ListByIdsAsync(
        IReadOnlyCollection<SellerRoleId> ids, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(Guid sellerId, string name, CancellationToken cancellationToken = default);

    Task AddAsync(SellerRole role, CancellationToken cancellationToken = default);

    void Remove(SellerRole role);
}
