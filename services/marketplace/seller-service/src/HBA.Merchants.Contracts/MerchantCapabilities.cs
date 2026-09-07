namespace HBA.Merchants.Contracts;

/// <summary>LES CODES DE PERMISSION, CÔTÉ APPELANT.</summary>
public static class MerchantCapabilities
{
    // ── Catalogue ───────────────────────────────────────────────────────────
    public const string ProductView = "PRODUCT_VIEW";
    public const string ProductCreate = "PRODUCT_CREATE";
    public const string ProductUpdate = "PRODUCT_UPDATE";
    public const string ProductSubmitForReview = "PRODUCT_SUBMIT_FOR_REVIEW";
    public const string ProductPublish = "PRODUCT_PUBLISH";
    public const string ProductUnpublish = "PRODUCT_UNPUBLISH";

    // ── Offres ──────────────────────────────────────────────────────────────

    /// <summary>Créer, retirer une offre, en changer le délai de préparation.</summary>
    public const string OfferManage = "OFFER_MANAGE";

    /// <summary>Changer le prix d'une offre, ou lui poser une promotion.</summary>
    public const string OfferPriceUpdate = "OFFER_PRICE_UPDATE";

    // ── Stock ───────────────────────────────────────────────────────────────
    public const string InventoryView = "INVENTORY_VIEW";
    public const string InventoryAdjust = "INVENTORY_ADJUST";
    public const string InventoryTransfer = "INVENTORY_TRANSFER";
    public const string StockMovementView = "STOCK_MOVEMENT_VIEW";

    /// <summary>Voir les lieux d'expédition du vendeur.</summary>
    public const string StockLocationView = "STOCK_LOCATION_VIEW";

    /// <summary>Créer, modifier ou supprimer un lieu d'expédition.</summary>
    public const string StockLocationManage = "STOCK_LOCATION_MANAGE";

    // ── Commandes ───────────────────────────────────────────────────────────
    public const string OrderView = "ORDER_VIEW";
    public const string OrderConfirm = "ORDER_CONFIRM";
    public const string OrderReject = "ORDER_REJECT";
    public const string OrderMarkPreparing = "ORDER_MARK_PREPARING";
    public const string OrderMarkReady = "ORDER_MARK_READY";
    public const string OrderCancel = "ORDER_CANCEL";

    // ── Avis ────────────────────────────────────────────────────────────────
    public const string ReviewView = "REVIEW_VIEW";
    public const string ReviewReply = "REVIEW_REPLY";

    // ── Retours ─────────────────────────────────────────────────────────────
    public const string ReturnView = "RETURN_VIEW";
    public const string ReturnApprove = "RETURN_APPROVE";
    public const string ReturnReject = "RETURN_REJECT";
    public const string ReturnConfirmReceived = "RETURN_CONFIRM_RECEIVED";
    public const string ReturnInspect = "RETURN_INSPECT";
    public const string ReturnDisputeView = "RETURN_DISPUTE_VIEW";

    // ── Équipe et rôles ─────────────────────────────────────────────────────
    public const string MemberView = "MEMBER_VIEW";
    public const string MemberInvite = "MEMBER_INVITE";
    public const string MemberSuspend = "MEMBER_SUSPEND";
    public const string MemberRevoke = "MEMBER_REVOKE";
    public const string MemberAssignStore = "MEMBER_ASSIGN_STORE";
    public const string MemberAssignRole = "MEMBER_ASSIGN_ROLE";
    public const string RoleView = "ROLE_VIEW";
    public const string RoleCreate = "ROLE_CREATE";
    public const string RoleUpdate = "ROLE_UPDATE";
    public const string RoleDelete = "ROLE_DELETE";
    public const string RoleAssign = "ROLE_ASSIGN";

    // ── Finances ────────────────────────────────────────────────────────────
    public const string FinanceView = "FINANCE_VIEW";
    public const string WalletView = "WALLET_VIEW";
    public const string PayoutView = "PAYOUT_VIEW";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE. Aucun rôle ne peut la porter.</summary>
    public const string PayoutConfigure = "PAYOUT_CONFIGURE";

    public const string WithdrawalRequest = "WITHDRAWAL_REQUEST";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE.</summary>
    public const string BankAccountUpdate = "BANK_ACCOUNT_UPDATE";

    // ── Le dossier vendeur lui-même ─────────────────────────────────────────
    public const string SellerProfileView = "SELLER_PROFILE_VIEW";
    public const string SellerProfileUpdate = "SELLER_PROFILE_UPDATE";
    public const string KybManage = "KYB_MANAGE";
    public const string StoreView = "STORE_VIEW";
    public const string StoreCreate = "STORE_CREATE";
    public const string StoreUpdate = "STORE_UPDATE";
    public const string StoreOpenClose = "STORE_OPEN_CLOSE";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE.</summary>
    public const string SellerClose = "SELLER_CLOSE";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE.</summary>
    public const string SellerReactivate = "SELLER_REACTIVATE";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE.</summary>
    public const string OwnershipTransfer = "OWNERSHIP_TRANSFER";

    /// <summary>RÉSERVÉE AU PROPRIÉTAIRE.</summary>
    public const string SecurityPolicyUpdate = "SECURITY_POLICY_UPDATE";

    public const string AuditView = "AUDIT_VIEW";

    // ── Analytique ──────────────────────────────────────────────────────────

    /// <summary>
    /// Lire les chiffres de vente agreges du vendeur — <c> GET
    /// /api/sellers/{sellerId}/analytics/sales</c>.
    /// </summary>
    public const string SellerAnalyticsView = "SELLER_ANALYTICS_VIEW";

    /// <summary>Tous les codes déclarés ici, par réflexion sur les constantes.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        .. typeof(MerchantCapabilities)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order()
    ];

    /// <summary>
    /// Les capacités CRITIQUES — celles qui exigent une authentification récente en
    /// plus de la permission (§37).
    /// </summary>
    public static IReadOnlySet<string> Critical { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PayoutConfigure,
        WithdrawalRequest,
        BankAccountUpdate,
        SellerClose,
        OwnershipTransfer,
        SecurityPolicyUpdate
    };

    /// <summary>Ce code exige-t-il une authentification récente ?</summary>
    public static bool RequiresStepUp(string capability) => Critical.Contains(capability);
}
