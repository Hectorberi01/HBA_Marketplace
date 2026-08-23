namespace HBA.Merchants.Contracts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CODES DE PERMISSION, CÔTÉ APPELANT.
///
/// POURQUOI CES CONSTANTES EXISTENT ALORS QUE L'ÉNUMÉRATION EXISTE DÉJÀ.
///
/// `MerchantPermission` vit dans le DOMAINE de seller-service. Un service appelant
/// n'a aucune raison de référencer `HBA.Merchants.Domain` — ce serait ouvrir la
/// porte à ses agrégats dans catalog, et la frontière ne tiendrait pas six mois.
/// Sans ces constantes, les ~72 routes du lot D1 écriraient des chaînes à la main,
/// et une faute de frappe produirait un refus silencieux : la permission demandée
/// n'existe simplement dans aucun rôle, donc personne ne l'a jamais.
///
/// CE FICHIER ET L'ÉNUMÉRATION SONT TENUS SYNCHRONES PAR UN TEST.
///
/// `MerchantCapabilities` étant côté contrat et `MerchantPermission` côté domaine,
/// rien dans le compilateur ne les relie. `CapabilitesTests` vérifie que chaque
/// constante d'ici correspond au code d'une permission réelle, et que le catalogue
/// n'en porte aucune qui manquerait ici. Sans ce test, les deux listes
/// divergeraient au premier ajout — et la divergence se manifesterait par un refus
/// que personne ne saurait expliquer.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>
    /// Changer le prix d'une offre, ou lui poser une promotion.
    /// <para>
    /// SÉPARÉE DE <see cref="OfferManage"/> : passer un article à 1 F CFA le
    /// liquide avant qu'aucune alerte ne parte, et les commandes passées à ce prix
    /// sont dues.
    /// </para>
    /// </summary>
    public const string OfferPriceUpdate = "OFFER_PRICE_UPDATE";

    // ── Stock ───────────────────────────────────────────────────────────────
    public const string InventoryView = "INVENTORY_VIEW";
    public const string InventoryAdjust = "INVENTORY_ADJUST";
    public const string InventoryTransfer = "INVENTORY_TRANSFER";
    public const string StockMovementView = "STOCK_MOVEMENT_VIEW";

    /// <summary>Voir les lieux d'expédition du vendeur.</summary>
    public const string StockLocationView = "STOCK_LOCATION_VIEW";

    /// <summary>
    /// Créer, modifier ou supprimer un lieu d'expédition.
    /// <para>
    /// SENSIBLE : c'est l'adresse d'où partent les colis, et elle voyage
    /// jusqu'à delivery pour bâtir l'enlèvement coursier. La repointer détourne
    /// des marchandises, pas seulement des données.
    /// </para>
    /// </summary>
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
    //
    // DÉCLARÉES, SANS RIEN À GARDER. `return-refund-service` est un squelette :
    // quatre csproj, un Program.cs de dix-huit lignes, aucune entité. Elles
    // existent parce que le cahier le demande (§27) et qu'un catalogue coûte une
    // ligne ; elles ne seront appliquées que le jour où le service existera.
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

    /// <summary>
    /// Tous les codes déclarés ici, par réflexion sur les constantes.
    /// </summary>
    /// <remarks>
    /// PAR RÉFLEXION, ET NON PAR UNE SECONDE LISTE ÉCRITE À LA MAIN.
    ///
    /// Une liste recopiée serait une troisième source à maintenir, après
    /// l'énumération et ces constantes. Elle divergerait exactement comme les deux
    /// autres, mais sans qu'aucun test ne puisse s'en apercevoir — puisqu'elle
    /// servirait justement à les comparer.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } =
    [
        .. typeof(MerchantCapabilities)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order()
    ];

    /// <summary>
    /// Les capacités CRITIQUES — celles qui exigent une authentification récente
    /// en plus de la permission (§37).
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE QU'ELLES ONT EN COMMUN : ELLES DÉPLACENT DE L'ARGENT, OU L'ACCÈS.
    ///
    /// Configurer un compte de versement, demander un virement, changer des
    /// coordonnées bancaires, fermer le dossier, transférer la propriété, modifier
    /// la politique de sécurité. Toutes sont irréversibles à l'échelle de la
    /// journée, et toutes sont exactement ce qu'on fait d'un poste laissé ouvert
    /// au marché. La permission dit « ce rôle a le droit » ; le step-up dit
    /// « et c'est bien lui qui est devant l'écran, maintenant ».
    ///
    /// CETTE LISTE EST ÉCRITE À LA MAIN ICI, ET UN TEST LA TIENT.
    ///
    /// Le niveau de risque vit dans `MerchantPermissions.Catalogue`, côté DOMAINE
    /// de seller-service — hors de portée d'un service appelant, et c'est voulu.
    /// `CapacitesTests` vérifie que cet ensemble correspond exactement à celui du
    /// catalogue : une permission promue Critique sans être ajoutée ici resterait
    /// silencieusement sans step-up, ce qui est le défaut le moins visible qui
    /// soit — tout continue de fonctionner, simplement moins bien gardé.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
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
