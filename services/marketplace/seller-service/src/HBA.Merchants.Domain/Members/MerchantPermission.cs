namespace HBA.Merchants.Domain.Members;

/// <summary>
/// LE CATALOGUE DES PERMISSIONS — UNE ÉNUMÉRATION, ET UNE TABLE QUI LA PROJETTE.
///
/// POURQUOI UNE ÉNUMÉRATION ALORS QUE LE CAHIER DEMANDE UNE TABLE.
///
/// Les deux, et chacune pour ce qu'elle sait faire. Les RÔLES sont des lignes,
/// parce que le vendeur peut en créer (§18) : leur liste n'est pas connue à la
/// compilation. Les PERMISSIONS, elles, le sont — chacune correspond à une route
/// précise, et c'est du code qui écrira `Require(MerchantPermission.OrderConfirm)`.
/// Une permission qui ne vivrait qu'en base ne serait jamais qu'une chaîne : une
/// faute de frappe compilerait, et la garde laisserait passer.
///
/// La table `permissions` du §12 existe donc, mais comme PROJECTION de cette
/// énumération, semée au démarrage. Elle sert au catalogue rendu par
/// `GET /permissions` et aux clés étrangères de `role_permissions` ; elle n'est
/// jamais la source.
///
/// LES CODES SONT EN MAJUSCULES, CONTRAIREMENT À `FoodPermission`.
///
/// Le précédent de food écrit `restaurant.order.accept`. Le cahier des membres
/// écrit `ORDER_CONFIRM`, et ce code voyage dans `error.details.requiredPermission`
/// (§47) — donc jusque dans l'application vendeur. On suit le cahier ; l'écart
/// avec food est assumé et se voit ici plutôt que de se découvrir à l'usage.
///
/// NE JAMAIS RÉUTILISER UN ENTIER LIBÉRÉ.
///
/// La valeur est persistée dans `role_permissions`. Retirer une permission puis
/// donner son numéro à une autre transformerait silencieusement les droits déjà
/// accordés. On ajoute à la fin ; on ne recycle pas.
/// </summary>
public enum MerchantPermission
{
    // ── Catalogue (§10.1) ───────────────────────────────────────────────────
    ProductView = 0,
    ProductCreate = 1,
    ProductUpdate = 2,
    ProductSubmitForReview = 3,
    ProductPublish = 4,
    ProductUnpublish = 5,

    // ── Offres ──────────────────────────────────────────────────────────────
    OfferManage = 6,
    OfferPriceUpdate = 7,

    // ── Stock ───────────────────────────────────────────────────────────────
    InventoryView = 10,
    InventoryAdjust = 11,
    InventoryTransfer = 12,
    StockMovementView = 13,

    // AJOUTÉES AU LOT D1, PARCE QUE TROIS ROUTES N'AVAIENT RIEN À EXIGER.
    StockLocationView = 14,
    StockLocationManage = 15,

    // ── Commandes ───────────────────────────────────────────────────────────
    OrderView = 20,
    OrderConfirm = 21,
    OrderReject = 22,
    OrderMarkPreparing = 23,
    OrderMarkReady = 24,
    OrderCancel = 25,

    // ── Avis ────────────────────────────────────────────────────────────────
    ReviewView = 30,
    ReviewReply = 31,

    // ── Retours ─────────────────────────────────────────────────────────────
    ReturnView = 40,
    ReturnApprove = 41,
    ReturnReject = 42,
    ReturnConfirmReceived = 43,
    ReturnInspect = 44,
    ReturnDisputeView = 45,

    // ── Équipe et rôles ─────────────────────────────────────────────────────
    MemberView = 50,
    MemberInvite = 51,
    MemberSuspend = 52,
    MemberRevoke = 53,
    MemberAssignStore = 54,
    MemberAssignRole = 55,
    RoleView = 56,
    RoleCreate = 57,
    RoleUpdate = 58,
    RoleDelete = 59,
    RoleAssign = 60,

    // ── Finances ────────────────────────────────────────────────────────────
    FinanceView = 70,
    WalletView = 71,
    PayoutView = 72,
    PayoutConfigure = 73,
    WithdrawalRequest = 74,
    BankAccountUpdate = 75,

    // ── Le dossier vendeur lui-même ─────────────────────────────────────────
    SellerProfileView = 80,
    SellerProfileUpdate = 81,
    KybManage = 82,
    StoreView = 83,
    StoreCreate = 84,
    StoreUpdate = 85,
    StoreOpenClose = 86,
    SellerClose = 87,
    SellerReactivate = 88,
    OwnershipTransfer = 89,
    SecurityPolicyUpdate = 90,
    AuditView = 91,

    // ── Analytique ──────────────────────────────────────────────────────────
    SellerAnalyticsView = 92
}

/// <summary>Niveau de risque du §12 (<c>permissions.risk_level</c>).</summary>
public enum PermissionRisk
{
    Normal = 0,
    Sensitive = 1,
    Critical = 2
}

/// <summary>Portée d'un rôle. C'est sa VOCATION, pas son application immédiate.</summary>
public enum RoleScope
{
    Seller = 0,
    Store = 1
}

/// <summary>
/// Ce que le dépôt sait de chaque permission : son code public, son risque, et si
/// elle est réservée au propriétaire.
/// </summary>
public static class MerchantPermissions
{
    private readonly record struct Entree(
        MerchantPermission Permission, string Code, PermissionRisk Risk, bool OwnerOnly);

    private static readonly Entree[] Catalogue =
    [
        new(MerchantPermission.ProductView, "PRODUCT_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.ProductCreate, "PRODUCT_CREATE", PermissionRisk.Normal, false),
        new(MerchantPermission.ProductUpdate, "PRODUCT_UPDATE", PermissionRisk.Normal, false),
        new(MerchantPermission.ProductSubmitForReview, "PRODUCT_SUBMIT_FOR_REVIEW", PermissionRisk.Normal, false),

        // NE CONTOURNE JAMAIS LA VALIDATION ADMINISTRATIVE DU CATALOGUE (§11).
        new(MerchantPermission.ProductPublish, "PRODUCT_PUBLISH", PermissionRisk.Normal, false),
        new(MerchantPermission.ProductUnpublish, "PRODUCT_UNPUBLISH", PermissionRisk.Normal, false),

        // SENSIBLES TOUTES DEUX : elles agissent sur la VITRINE et sur ce qui se
        // facture, pas sur un brouillon interne.
        new(MerchantPermission.OfferManage, "OFFER_MANAGE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.OfferPriceUpdate, "OFFER_PRICE_UPDATE", PermissionRisk.Sensitive, false),

        new(MerchantPermission.InventoryView, "INVENTORY_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.InventoryAdjust, "INVENTORY_ADJUST", PermissionRisk.Sensitive, false),
        new(MerchantPermission.InventoryTransfer, "INVENTORY_TRANSFER", PermissionRisk.Sensitive, false),
        new(MerchantPermission.StockMovementView, "STOCK_MOVEMENT_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.StockLocationView, "STOCK_LOCATION_VIEW", PermissionRisk.Normal, false),

        // SENSIBLE : un lieu d'expédition est l'adresse d'où partent les colis, et
        // elle voyage jusqu'à delivery pour bâtir l'enlèvement coursier.
        new(MerchantPermission.StockLocationManage, "STOCK_LOCATION_MANAGE", PermissionRisk.Sensitive, false),

        new(MerchantPermission.OrderView, "ORDER_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.OrderConfirm, "ORDER_CONFIRM", PermissionRisk.Normal, false),
        new(MerchantPermission.OrderReject, "ORDER_REJECT", PermissionRisk.Normal, false),
        new(MerchantPermission.OrderMarkPreparing, "ORDER_MARK_PREPARING", PermissionRisk.Normal, false),
        new(MerchantPermission.OrderMarkReady, "ORDER_MARK_READY", PermissionRisk.Normal, false),
        new(MerchantPermission.OrderCancel, "ORDER_CANCEL", PermissionRisk.Sensitive, false),

        new(MerchantPermission.ReviewView, "REVIEW_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.ReviewReply, "REVIEW_REPLY", PermissionRisk.Normal, false),

        // CES SIX-LÀ N'ONT RIEN À GARDER AUJOURD'HUI.
        new(MerchantPermission.ReturnView, "RETURN_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.ReturnApprove, "RETURN_APPROVE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.ReturnReject, "RETURN_REJECT", PermissionRisk.Sensitive, false),
        new(MerchantPermission.ReturnConfirmReceived, "RETURN_CONFIRM_RECEIVED", PermissionRisk.Normal, false),
        new(MerchantPermission.ReturnInspect, "RETURN_INSPECT", PermissionRisk.Normal, false),
        new(MerchantPermission.ReturnDisputeView, "RETURN_DISPUTE_VIEW", PermissionRisk.Normal, false),

        new(MerchantPermission.MemberView, "MEMBER_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.MemberInvite, "MEMBER_INVITE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.MemberSuspend, "MEMBER_SUSPEND", PermissionRisk.Sensitive, false),
        new(MerchantPermission.MemberRevoke, "MEMBER_REVOKE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.MemberAssignStore, "MEMBER_ASSIGN_STORE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.MemberAssignRole, "MEMBER_ASSIGN_ROLE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.RoleView, "ROLE_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.RoleCreate, "ROLE_CREATE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.RoleUpdate, "ROLE_UPDATE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.RoleDelete, "ROLE_DELETE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.RoleAssign, "ROLE_ASSIGN", PermissionRisk.Sensitive, false),

        new(MerchantPermission.FinanceView, "FINANCE_VIEW", PermissionRisk.Sensitive, false),
        new(MerchantPermission.WalletView, "WALLET_VIEW", PermissionRisk.Sensitive, false),
        new(MerchantPermission.PayoutView, "PAYOUT_VIEW", PermissionRisk.Sensitive, false),

        // LES TROIS QUI DÉTOURNENT L'ARGENT, ET LA RAISON DE `OwnerOnly`.
        new(MerchantPermission.PayoutConfigure, "PAYOUT_CONFIGURE", PermissionRisk.Critical, true),
        new(MerchantPermission.WithdrawalRequest, "WITHDRAWAL_REQUEST", PermissionRisk.Critical, false),
        new(MerchantPermission.BankAccountUpdate, "BANK_ACCOUNT_UPDATE", PermissionRisk.Critical, true),

        new(MerchantPermission.SellerProfileView, "SELLER_PROFILE_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.SellerProfileUpdate, "SELLER_PROFILE_UPDATE", PermissionRisk.Normal, false),
        new(MerchantPermission.KybManage, "KYB_MANAGE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.StoreView, "STORE_VIEW", PermissionRisk.Normal, false),
        new(MerchantPermission.StoreCreate, "STORE_CREATE", PermissionRisk.Sensitive, false),
        new(MerchantPermission.StoreUpdate, "STORE_UPDATE", PermissionRisk.Normal, false),
        new(MerchantPermission.StoreOpenClose, "STORE_OPEN_CLOSE", PermissionRisk.Normal, false),

        new(MerchantPermission.SellerClose, "SELLER_CLOSE", PermissionRisk.Critical, true),
        new(MerchantPermission.SellerReactivate, "SELLER_REACTIVATE", PermissionRisk.Sensitive, true),
        new(MerchantPermission.OwnershipTransfer, "OWNERSHIP_TRANSFER", PermissionRisk.Critical, true),

        // Déclarée, sans objet tant que `seller_security_policies` n'existe pas.
        new(MerchantPermission.SecurityPolicyUpdate, "SECURITY_POLICY_UPDATE", PermissionRisk.Critical, true),

        new(MerchantPermission.AuditView, "AUDIT_VIEW", PermissionRisk.Sensitive, false),

        // NORMAL, PAS SENSIBLE : des chiffres de vente agreges, pas un mouvement
        // d'argent.
        new(MerchantPermission.SellerAnalyticsView, "SELLER_ANALYTICS_VIEW", PermissionRisk.Normal, false)
    ];

    private static readonly Dictionary<MerchantPermission, Entree> ParPermission;
    private static readonly Dictionary<string, MerchantPermission> ParCode;

    /// <summary>LE GARDE-FOU QUI REND CETTE TABLE SÛRE.</summary>
    static MerchantPermissions()
    {
        ParPermission = Catalogue.ToDictionary(e => e.Permission);
        ParCode = Catalogue.ToDictionary(e => e.Code, e => e.Permission, StringComparer.Ordinal);

        var manquantes = Enum.GetValues<MerchantPermission>()
            .Where(p => !ParPermission.ContainsKey(p))
            .ToArray();

        if (manquantes.Length > 0)
        {
            throw new InvalidOperationException(
                "Permissions déclarées dans l'énumération et absentes du catalogue : "
                + string.Join(", ", manquantes)
                + ". Ajoutez-leur une ligne dans MerchantPermissions.Catalogue.");
        }

        if (Catalogue.Length != ParCode.Count)
        {
            throw new InvalidOperationException("Deux permissions partagent le même code public.");
        }
    }

    /// <summary>Toutes les permissions, dans l'ordre du catalogue.</summary>
    public static IReadOnlyList<MerchantPermission> All { get; }
        = [.. Catalogue.Select(e => e.Permission)];

    /// <summary>
    /// Le code public — celui qui voyage dans <c>
    /// error.details.requiredPermission</c>.
    /// </summary>
    public static string ToCode(this MerchantPermission permission) => ParPermission[permission].Code;

    public static PermissionRisk RiskOf(this MerchantPermission permission) => ParPermission[permission].Risk;

    /// <summary>
    /// Réservée au propriétaire : aucun rôle, même personnalisé, ne peut la porter.
    /// </summary>
    public static bool IsOwnerOnly(this MerchantPermission permission) => ParPermission[permission].OwnerOnly;

    /// <summary>Le code public d'une permission, ou <c>null</c> si le code est inconnu.</summary>
    public static MerchantPermission? Parse(string? code)
        => code is not null && ParCode.TryGetValue(code, out var permission) ? permission : null;

    /// <summary>
    /// Les permissions réservées au propriétaire — le sous-ensemble qu'aucune
    /// délégation ne peut franchir (§11).
    /// </summary>
    public static IReadOnlySet<MerchantPermission> OwnerOnly { get; }
        = Catalogue.Where(e => e.OwnerOnly).Select(e => e.Permission).ToHashSet();

    /// <summary>
    /// LES PERMISSIONS QUI NE GARDENT AUCUNE ROUTE — DÉCLARÉES, ET SEULEMENT
    /// CELLES-LÀ.
    /// </summary>
    public static IReadOnlySet<MerchantPermission> SansGardeAssumee { get; } = new HashSet<MerchantPermission>
    {
        // La fonctionnalité N'EXISTE PAS. Aucune notion de litige dans
        // return-refund-service : les cinq autres `RETURN_*` gardent enfin des
        // routes depuis le lot retours, celle-ci n'a rien à garder.
        MerchantPermission.ReturnDisputeView,

        // La fonctionnalité EXISTE, SOUS UNE GARDE PLUS LARGE, et c'est correct :
        // `GET /api/engagement/reviews/seller/{id}` est ouvert à tout compte
        // authentifié parce que les avis sont PUBLICS — un acheteur les lit sur la
        // fiche produit.
        MerchantPermission.ReviewView,

        // DOUBLON SÉMANTIQUE de `MEMBER_ASSIGN_ROLE`, qui garde bien `PUT
        // /members/{id}/roles`.
        MerchantPermission.RoleAssign,

        // MÊME CAS : `PUT /{sellerId}/payout-account` exige `PAYOUT_CONFIGURE`, et
        // `BankAccount` n'est qu'une valeur de `PayoutProvider`.
        MerchantPermission.BankAccountUpdate,

        // Sans objet tant que `seller_security_policies` n'existe pas — c'est déjà
        // écrit à sa ligne de catalogue.
        MerchantPermission.SecurityPolicyUpdate
    };

    /// <summary>
    /// Les permissions critiques — l'ensemble exact que le lot 0b soumettra au
    /// step-up (§37).
    /// </summary>
    public static IReadOnlySet<MerchantPermission> Critical { get; }
        = Catalogue.Where(e => e.Risk == PermissionRisk.Critical).Select(e => e.Permission).ToHashSet();

    /// <summary>Les permissions dont l'application est RÉELLEMENT cadrée par boutique.</summary>
    public static IReadOnlySet<MerchantPermission> StoreScoped { get; } = new HashSet<MerchantPermission>
    {
        MerchantPermission.ProductView,
        MerchantPermission.ProductCreate,
        MerchantPermission.ProductUpdate,
        MerchantPermission.ProductSubmitForReview,
        MerchantPermission.ProductPublish,
        MerchantPermission.ProductUnpublish,
        MerchantPermission.OfferManage,
        MerchantPermission.OfferPriceUpdate,
        MerchantPermission.StoreView,
        MerchantPermission.StoreUpdate,
        MerchantPermission.StoreOpenClose
    };

    /// <summary>Cette permission est-elle réellement cadrée par boutique ?</summary>
    public static bool IsStoreScoped(this MerchantPermission permission) => StoreScoped.Contains(permission);
}
