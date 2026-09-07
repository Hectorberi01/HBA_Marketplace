namespace HBA.Food.Contracts;

/// <summary>Un créneau de service, tel qu'affiché.</summary>
public sealed record ServiceHoursSummary(string Day, string OpensAt, string ClosesAt);

/// <summary>L'APPARTENANCE D'UN COMPTE AU PERSONNEL D'UN RESTAURANT.</summary>
public sealed record FoodStaffMembership(
    Guid RestaurantId,
    Guid StaffId,
    Guid UserId,
    string Role,
    bool IsActive,
    bool IsFounder,
    IReadOnlyList<string> Permissions)
{
    /// <summary>
    /// UN MEMBRE DÉSACTIVÉ N'A AUCUNE PERMISSION — le domaine rend déjà une liste
    /// vide.
    /// </summary>
    public bool Can(string permissionCode)
        => IsActive && Permissions.Contains(permissionCode, StringComparer.Ordinal);
}

/// <summary>Un membre du personnel, tel qu'affiché dans l'espace du restaurateur.</summary>
/// <param name="IsFounder">
/// <summary> Le compte à l'origine de l'établissement : ni rétrogradable, ni
/// désactivable.</summary>
/// </param>
/// <param name="Permissions">
/// <summary> Ce que ce membre peut RÉELLEMENT faire : son rôle, corrigé de ses
/// dérogations.</summary>
/// </param>
/// <param name="Overrides">Les seules dérogations NOMMÉES, sans les défauts du rôle.</param>
public sealed record StaffMemberSummary(
    Guid Id,
    Guid UserId,
    string Role,
    bool IsActive,
    bool IsFounder,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<StaffPermissionOverrideSummary> Overrides,

    DateTime CreatedOnUtc);

/// <summary>Une dérogation nominative.</summary>
public sealed record StaffPermissionOverrideSummary(string Permission, bool IsGranted);

// ── Postes de préparation (§9) ──────────────────────────────────────────────

/// <summary>Un poste : GRILL, PIZZA, DRINKS.</summary>
public sealed record PreparationStationView(
    Guid Id, string Name, string Code, bool IsActive, int DisplayOrder);

// ── Commandes Food (§10 à §13) ──────────────────────────────────────────────

public sealed record FoodOrderItemOptionView(string GroupName, string OptionName, decimal PriceDelta);

/// <summary>Une ligne de commande, FIGÉE (§13).</summary>
public sealed record FoodOrderItemView(
    Guid Id,
    Guid MenuItemId,
    string NameSnapshot,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string? Notes,
    string KitchenStatus,
    Guid? PreparationStationId,
    IReadOnlyList<FoodOrderItemOptionView> Options);

public sealed record FoodOrderRejectionView(string Reason, string? Comment, DateTime RejectedAtUtc);

/// <summary>Une commande, côté restaurant.</summary>
public sealed record FoodOrderView(
    Guid Id,
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    string KitchenStatus,
    decimal Total,
    string Currency,
    string? CustomerNote,
    int? EstimatedPreparationMinutes,
    int Priority,
    DateTime ReceivedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? ReadyAtUtc,
    DateTime? PickedUpAtUtc,
    FoodOrderRejectionView? Rejection,
    IReadOnlyList<FoodOrderItemView> Items);

/// <summary>Une ligne du ticket, telle qu'affichée en cuisine.</summary>
/// <param name="Options">
/// <summary> « Taille : Grande », « Sauce : Mayo » — déjà mises en forme pour
/// l'écran.</summary>
/// </param>
public sealed record KitchenTicketItemView(
    Guid Id,
    string Name,
    int Quantity,
    string? Notes,
    string Status,
    Guid? PreparationStationId,
    int PreparationMinutes,
    IReadOnlyList<string> Options);

/// <summary>Un ticket sur l'écran de cuisine (§13).</summary>
public sealed record KitchenTicketView(
    Guid FoodOrderId,
    Guid OrderId,
    string Status,
    int Priority,
    int? EstimatedPreparationMinutes,
    DateTime ReceivedAtUtc,
    DateTime? AcceptedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? ReadyAtUtc,
    string? CustomerNote,
    int OtherStationsPending,
    IReadOnlyList<KitchenTicketItemView> Items);

/// <summary>Le tableau de cuisine complet, avec les postes disponibles pour le filtrer.</summary>
public sealed record KitchenBoardView(
    Guid RestaurantId,
    Guid? StationId,
    IReadOnlyList<PreparationStationView> Stations,
    IReadOnlyList<KitchenTicketView> Tickets);

/// <summary>LES CODES DE PERMISSION DU CAHIER DES CHARGES (§2).</summary>
public static class FoodPermissionCodes
{
    public const string OrderAccept = "restaurant.order.accept";
    public const string OrderReject = "restaurant.order.reject";
    public const string MenuManage = "restaurant.menu.manage";
    public const string StaffManage = "restaurant.staff.manage";
    public const string KitchenManage = "restaurant.kitchen.manage";
    public const string SettingsManage = "restaurant.settings.manage";
    public const string AnalyticsRead = "restaurant.analytics.read";

    /// <summary>
    /// Les sept codes, pour les tests de correspondance et les écrans
    /// d'administration.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OrderAccept, OrderReject, MenuManage, StaffManage, KitchenManage, SettingsManage, AnalyticsRead
    ];
}

/// <summary>
/// Un établissement, vu de l'extérieur du module.
///
/// PAS D'ADRESSE ICI. Le lieu physique vit dans Inventory et n'est référencé
/// que par son identifiant : recopier l'adresse créerait deux vérités pour un
/// même lieu, qui divergeraient au premier déménagement.
/// </summary>
/// <param name="LogoMediaId">
/// UN IDENTIFIANT DE MÉDIA, PLUS UNE URL.
///
/// Food ne connaît pas le service média — sa frontière l'interdit. C'est la
/// couche qui voit les deux qui résout l'adresse, en tenant compte de la
/// visibilité et des variantes. Renvoyer une URL d'ici obligerait Food à
/// connaître le CDN, et chaque changement de domaine à réécrire des tables.
/// </param>
/// <param name="LegacyLogoUrl">
/// <summary>TRANSITOIRE : l'URL d'avant la bascule, tant que les logos ne sont pas reversés.</summary>
/// </param>
/// <param name="AcceptsOrdersNow">
/// Prend-il une commande MAINTENANT ? Calculé à l'instant de la lecture — le
/// statut seul ne suffit pas, les horaires et la pause comptent aussi.
/// </param>
/// <param name="BlockedReason">
/// Pourquoi il n'en prend pas. « Indisponible » sans motif est la réponse la
/// plus frustrante qui soit : le client ne sait pas s'il doit revenir dans dix
/// minutes, demain, ou jamais.
/// </param>
/// <param name="AcceptanceMode">
/// <summary>« Manual » ou « Automatic » (§3).</summary>
/// </param>
/// <param name="MinimumOrderAmount">
/// <summary>Minimum de commande, hors livraison. Nul = aucun.</summary>
/// </param>
/// <param name="LoadLevel">
/// « Normal », « High », « Saturated » (§14).
///
/// CE N'EST PAS UN MOTIF DE BLOCAGE. Un restaurant saturé n'est pas fermé :
/// il est LENT. Le confondre avec <c>BlockedReason</c> ferait afficher
/// « revenez demain » à quelqu'un qui aurait très bien pu commander en
/// acceptant vingt minutes de plus. C'est le « forte demande » du cahier.
/// </param>
/// <param name="ExtraWaitMinutes">
/// <summary>Minutes ajoutées au délai annoncé par la charge actuelle.</summary>
/// </param>
/// <param name="SpecialClosureReason">
/// Pourquoi l'établissement est exceptionnellement fermé AUJOURD'HUI (§4) :
/// « Fête de l'Indépendance », « inventaire ». Nul si le jour est ordinaire.
///
/// SEULEMENT LE JOUR COURANT, pas toute la liste. C'est la seule chose
/// qu'un client a besoin de lire — « fermé » sans raison le fait revenir trois
/// fois. La liste complète appartient à l'écran du restaurateur.
/// </param>
/// <param name="PayoutSellerId">
/// Le dossier vendeur qui encaisse les recettes de l'établissement.
///
/// C'est par lui que passe TOUT le reversement : gains, portefeuille,
/// retrait, payout. Nul tant qu'aucun dossier n'est rattaché — et
/// l'établissement ne peut alors pas entrer en service.
/// </param>
/// <param name="IsPubliclyVisible">
/// A-t-il sa place dans la vitrine ?
///
/// CE N'EST PAS « accepte des commandes ». Un restaurant fermé le soir
/// reste VISIBLE — le client consulte sa carte et reviendra demain. Un
/// établissement non validé ou suspendu, lui, ne doit pas exister pour lui.
///
/// Le filtrage se fait chez l'APPELANT, pas ici : cette même API sert
/// l'espace du restaurateur, qui doit voir son dossier en brouillon, et la
/// file de validation, qui ne voit que des dossiers en attente.
/// </param>
public sealed record RestaurantSummary(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    Guid? LogoMediaId,
    Guid? CoverMediaId,
    string? LegacyLogoUrl,
    string Phone,
    string Status,
    bool AcceptsOrdersNow,
    string BlockedReason,

    int PreparationMinutes,
    string AcceptanceMode,
    decimal? MinimumOrderAmount,
    string LoadLevel,
    int ExtraWaitMinutes,
    string? SpecialClosureReason,

    Guid? FulfillmentLocationId,
    Guid? PayoutSellerId,
    IReadOnlyList<ServiceHoursSummary> ServiceHours,
    bool IsPubliclyVisible);

/// <summary>
/// API in-process du module Food.
///
/// ELLE SERT À AUTORISER, PAS SEULEMENT À AFFICHER. Le panier Food et la
/// commande s'en serviront pour refuser un repas d'un restaurant fermé — d'où
/// <c>AcceptsOrdersNow</c>, calculé et non stocké.
/// </summary>
/// <summary>
/// Une carte d'établissement dans la VITRINE — la liste que parcourt un client.
/// </summary>
/// <remarks>
/// CE N'EST PAS UN <see cref="RestaurantSummary"/> ALLÉGÉ. C'EST UNE PROMESSE
///    PLUS FAIBLE, ET DÉLIBÉRÉMENT.
///
/// `RestaurantSummary.AcceptsOrdersNow` est une réponse FERME : lieu ouvert,
/// horaires bons, pause levée, ET au moins un plat commandable sur une carte
/// servie à cette heure. Cette dernière vérification coûte jusqu'à quatre
/// requêtes par établissement — sur une page de vingt, quatre-vingts requêtes
/// pour un écran de parcours.
///
/// D'où <see cref="IsOpenNow"/>, qui ne parle que du LIEU : ouvert, pas en pause,
/// pas fermé exceptionnellement. La disponibilité réelle de la carte est
/// confirmée sur la fiche du restaurant, où l'on n'en interroge qu'un.
///
/// NE JAMAIS RENOMMER `IsOpenNow` EN `AcceptsOrdersNow`.
///
/// Les deux noms disent deux choses différentes, et le second engage. Un client
/// qui traverse la ville sur la foi d'une liste, pour découvrir que tout est
/// épuisé, ne revient pas. La liste dit « ouvert » ; la fiche dit « commandable ».
/// </remarks>
/// <param name="LogoMediaId">
/// <summary>Identifiant de média (§6). L'URL se résout hors du module.</summary>
/// </param>
/// <param name="LegacyLogoUrl">
/// <summary>TRANSITOIRE : l'URL d'avant la bascule vers media-service.</summary>
/// </param>
/// <param name="IsOpenNow">
/// <summary>Le LIEU est-il ouvert ? Ne dit rien de la carte — cf. remarques.</summary>
/// </param>
/// <param name="ClosedReason">
/// <summary>Pourquoi il ne l'est pas. « None » quand il l'est.</summary>
/// </param>
/// <param name="LoadLevel">
/// <summary>« Normal », « High », « Saturated ». Saturé n'est PAS fermé.</summary>
/// </param>
/// <param name="SpecialClosureReason">
/// <summary>Motif d'une fermeture exceptionnelle AUJOURD'HUI, s'il y en a une.</summary>
/// </param>
public sealed record RestaurantCardView(
    Guid Id,
    string Name,
    string? Description,
    Guid? LogoMediaId,
    string? LegacyLogoUrl,
    bool IsOpenNow,
    string ClosedReason,

    int PreparationMinutes,
    decimal? MinimumOrderAmount,
    string LoadLevel,

    int ExtraWaitMinutes,
    string? SpecialClosureReason);

public interface IFoodModuleApi
{
    Task<RestaurantSummary?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<RestaurantSummary?> GetRestaurantByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>OÙ CE COMPTE TRAVAILLE-T-IL, ET AVEC QUELS DROITS ?</summary>
    Task<FoodStaffMembership?> GetStaffMembershipAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>À QUELLE COMMANDE ET À QUEL RESTAURANT CE TICKET SE RATTACHE-T-IL ?</summary>
    Task<FoodOrderRef?> GetOrderAsync(Guid foodOrderId, CancellationToken cancellationToken = default);

    /// <summary>UN ARTICLE DE CARTE, AVEC SES GROUPES D'OPTIONS ET LEURS ÉCARTS DE PRIX.</summary>
    Task<MenuItemView?> GetMenuItemAsync(
        Guid restaurantId, Guid menuItemId, CancellationToken cancellationToken = default);
}

/// <summary>Les rattachements d'un ticket de cuisine, vus de l'extérieur du module.</summary>
/// <param name="Origin">
/// De quel univers vient <paramref name="OrderId"/> — voir
/// <see cref="IntegrationEvents.FoodOrderOrigins"/> .
/// </param>
public sealed record FoodOrderRef(
    Guid FoodOrderId,
    Guid OrderId,
    Guid RestaurantId,
    string Status,
    string Origin = IntegrationEvents.FoodOrderOrigins.Marketplace);

// ── La carte, telle qu'affichée ─────────────────────────────────────────────

/// <summary>Une option, et son écart de prix.</summary>
public sealed record OptionView(Guid Id, string Name, decimal PriceDelta, bool IsAvailable);

/// <summary>Un groupe de choix, avec ses règles.</summary>
public sealed record OptionGroupView(
    Guid Id, string Name, int MinSelections, int MaxSelections, bool IsRequired, IReadOnlyList<OptionView> Options);

/// <summary>Un article de la carte.</summary>
/// <param name="ImageMediaId">
/// <summary>Identifiant de média (§6). L'URL se résout hors du module.</summary>
/// </param>
/// <param name="LegacyImageUrl">
/// <summary>TRANSITOIRE : l'URL d'avant la bascule.</summary>
/// </param>
/// <param name="DisplayImageUrl">
/// L'adresse à afficher : `ImagePublicUrl` si le média est repris,
/// `LegacyImageUrl` sinon. Nulle quand l'article n'a pas de photo.
/// <remarks>
/// LE REPLI EST FAIT ICI, PAS DANS LES TROIS APPLICATIONS.
///
/// Client, vendeur et livreur afficheraient tous les trois le même
/// `imagePublicUrl ?? legacyImageUrl` — et le jour où `LegacyImageUrl` disparaît,
/// il faudrait trois publications de boutique pour le retirer. Les deux champs
/// bruts restent exposés pour qui doit distinguer un média repris d'un média
/// hérité ; celui-ci sert à AFFICHER.
/// </remarks>
/// </param>
/// <param name="IsOrderable">
/// Commandable MAINTENANT : PHOTO PRÉSENTE, disponible, et tous les groupes
/// obligatoires satisfiables.
/// </param>
/// <param name="HasImage">
/// L'article porte-t-il une photo ?
/// <remarks>
/// RENDU À CÔTÉ D'`IsOrderable`, ET NON DÉDUIT PAR LE CLIENT.
///
/// Depuis que la photo est obligatoire pour vendre, `IsOrderable == false` a
/// TROIS causes possibles : épuisé aujourd'hui, groupe d'options insatisfiable,
/// ou photo manquante. Les trois n'appellent pas le même geste — la première se
/// résout d'elle-même demain, la dernière attend une action.
///
/// Sans ce champ, l'espace restaurateur afficherait « indisponible » et ferait
/// attendre quelqu'un qui devrait agir. Le calculer côté client à partir de
/// `ImageMediaId` et `LegacyImageUrl` marcherait — et recopierait la règle
/// `HasImage` dans trois applications, où elle divergerait au premier changement.
/// </remarks>
/// </param>
/// <param name="BackAtUtc">
/// Quand il revient, si c'est connu. « De retour demain » vaut mieux
/// qu'« indisponible », qui ne dit pas s'il faut revenir dans dix minutes ou
/// jamais.
/// </param>
public sealed record MenuItemView(
    Guid Id,
    string Name,
    string? Description,
    Guid? ImageMediaId,
    string? LegacyImageUrl,
    string? DisplayImageUrl,
    decimal BasePrice,
    string Currency,
    bool IsOrderable,
    bool HasImage,
    DateTime? BackAtUtc,

    IReadOnlyList<OptionGroupView> OptionGroups);

/// <summary>Une section de carte : « Entrées », « Plats », « Boissons ».</summary>
public sealed record MenuSectionView(
    Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<MenuItemView> Items);

/// <summary>UNE CARTE : « Menu du midi », « Carte du soir », « Carte d'été ».</summary>
public sealed record MenuView(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsServedNow,
    string? ServedFrom,
    string? ServedUntil,
    string? AvailableFrom,
    string? AvailableUntil,
    IReadOnlyList<MenuSectionView> Sections);

/// <summary>La carte complète d'un restaurant, avec son état de service.</summary>
public sealed record RestaurantMenuView(
    Guid RestaurantId,
    string Name,
    bool AcceptsOrdersNow,
    string BlockedReason,
    int PreparationMinutes,
    IReadOnlyList<MenuView> Menus);

/// <summary>L'établissement du compte connecté, et son rôle dedans.</summary>
/// <param name="Role"><summary>« Owner », « Manager », « Cashier », « Cook »…</summary></param>
/// <param name="Permissions">
/// <summary> Permissions effectives, dérogations comprises.</summary>
/// </param>
/// <param name="PayoutSellerId">Le dossier vendeur qui encaisse.</param>
public sealed record PartnerRestaurantView(
    Guid RestaurantId,
    string Name,
    string Status,
    string Role,

    bool IsFounder,
    bool IsActive,
    IReadOnlyList<string> Permissions,
    Guid? PayoutSellerId,

    bool AcceptsOrdersNow,
    string BlockedReason);
