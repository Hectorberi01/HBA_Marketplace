namespace HBA.Food.Contracts;

/// <summary>Un créneau de service, tel qu'affiché. Heures au format « HH:mm ».</summary>
public sealed record ServiceHoursSummary(string Day, string OpensAt, string ClosesAt);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'APPARTENANCE D'UN COMPTE AU PERSONNEL D'UN RESTAURANT.
///
/// C'est ce que la couche HTTP lit pour répondre à deux questions à la fois :
/// DE QUEL établissement s'agit-il, et cette personne a-t-elle le droit de faire
/// ce qu'elle demande ?
///
/// LES RÔLES ET PERMISSIONS VOYAGENT EN CHAÎNES.
///
/// Ce sont les codes du cahier des charges — <c>restaurant.order.accept</c> — et
/// non les énumérations du domaine. Un appelant qui devrait référencer
/// <c>StaffRole</c> ferait exactement la dépendance que la frontière du module
/// interdit ; et ces codes-là sont ceux qui figureront dans les journaux d'audit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
    /// UN MEMBRE DÉSACTIVÉ N'A AUCUNE PERMISSION — le domaine rend déjà une
    /// liste vide. Ce test reste explicite ici pour que le contrôle ne dépende pas
    /// de ce qu'on a bien voulu mettre dans la liste.
    /// </summary>
    public bool Can(string permissionCode)
        => IsActive && Permissions.Contains(permissionCode, StringComparer.Ordinal);
}

/// <summary>Un membre du personnel, tel qu'affiché dans l'espace du restaurateur.</summary>
public sealed record StaffMemberSummary(
    Guid Id,
    Guid UserId,
    string Role,
    bool IsActive,

    /// <summary>Le compte à l'origine de l'établissement : ni rétrogradable, ni désactivable.</summary>
    bool IsFounder,

    /// <summary>Ce que ce membre peut RÉELLEMENT faire : son rôle, corrigé de ses dérogations.</summary>
    IReadOnlyList<string> Permissions,

    /// <summary>
    /// Les seules dérogations NOMMÉES, sans les défauts du rôle.
    ///
    /// Sans cette distinction, l'écran ne pourrait pas montrer ce qu'un
    /// propriétaire a décidé pour cette personne — tout se confondrait avec ce que
    /// le rôle donne, et personne ne saurait quoi retirer pour revenir au défaut.
    /// </summary>
    IReadOnlyList<StaffPermissionOverrideSummary> Overrides,

    DateTime CreatedOnUtc);

/// <summary>Une dérogation nominative. <c>IsGranted</c> distingue l'octroi du retrait.</summary>
public sealed record StaffPermissionOverrideSummary(string Permission, bool IsGranted);

// ── Postes de préparation (§9) ──────────────────────────────────────────────

/// <summary>Un poste : GRILL, PIZZA, DRINKS.</summary>
public sealed record PreparationStationView(
    Guid Id, string Name, string Code, bool IsActive, int DisplayOrder);

// ── Commandes Food (§10 à §13) ──────────────────────────────────────────────

public sealed record FoodOrderItemOptionView(string GroupName, string OptionName, decimal PriceDelta);

/// <summary>
/// Une ligne de commande, FIGÉE (§13).
///
/// <paramref name="NameSnapshot"/> et <paramref name="UnitPrice"/> sont ceux du
/// moment de l'achat, pas ceux de la carte d'aujourd'hui. C'est ce qui permet de
/// supprimer un plat sans réécrire l'histoire.
/// </summary>
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

/// <summary>
/// Une commande, côté restaurant.
///
/// DEUX STATUTS, ET ILS NE DISENT PAS LA MÊME CHOSE.
/// <paramref name="Status"/> est le cycle opérationnel de la commande ;
/// <paramref name="KitchenStatus"/> est l'état du ticket, DÉRIVÉ de ses lignes.
/// Les confondre ferait annoncer « prête » une commande dont le bar n'a pas
/// commencé.
/// </summary>
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
public sealed record KitchenTicketItemView(
    Guid Id,
    string Name,
    int Quantity,
    string? Notes,
    string Status,
    Guid? PreparationStationId,
    int PreparationMinutes,

    /// <summary>« Taille : Grande », « Sauce : Mayo » — déjà mises en forme pour l'écran.</summary>
    IReadOnlyList<string> Options);

/// <summary>
/// Un ticket sur l'écran de cuisine (§13).
///
/// <paramref name="OtherStationsPending"/> EST CE QUI EMPÊCHE UN POSTE DE
/// CROIRE QU'IL A FINI. Filtré par poste, un ticket ne montre que ses lignes — le
/// grillardin poserait ses burgers sur le passe et considérerait la commande
/// terminée, sans savoir que le bar n'a pas commencé.
/// </summary>
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CODES DE PERMISSION DU CAHIER DES CHARGES (§2).
///
/// CES CONSTANTES DOUBLENT <c>FoodPermission.ToCode()</c>, ET C'EST SUBI.
///
/// Le domaine ne peut pas référencer les contrats — la règle qui rend le module
/// extractible l'interdit dans ce sens comme dans l'autre. La couche HTTP, elle,
/// a besoin de ces chaînes pour déclarer ce que chaque route exige, et faire
/// transiter une énumération du domaine jusqu'à elle rouvrirait la dépendance.
///
/// La duplication est donc structurelle. Ce qui ne l'est pas, c'est la DÉRIVE :
/// un test — <c>Les_codes_de_permission_des_contrats_correspondent_au_domaine</c>
/// — compare les deux listes, valeur par valeur et en nombre. Renommer d'un côté
/// sans l'autre casse la compilation des tests, pas la production en silence.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class FoodPermissionCodes
{
    public const string OrderAccept = "restaurant.order.accept";
    public const string OrderReject = "restaurant.order.reject";
    public const string MenuManage = "restaurant.menu.manage";
    public const string StaffManage = "restaurant.staff.manage";
    public const string KitchenManage = "restaurant.kitchen.manage";
    public const string SettingsManage = "restaurant.settings.manage";
    public const string AnalyticsRead = "restaurant.analytics.read";

    /// <summary>Les sept codes, pour les tests de correspondance et les écrans d'administration.</summary>
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
public sealed record RestaurantSummary(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    /// <summary>
    /// UN IDENTIFIANT DE MÉDIA, PLUS UNE URL.
    ///
    /// Food ne connaît pas le service média — sa frontière l'interdit. C'est la
    /// couche qui voit les deux qui résout l'adresse, en tenant compte de la
    /// visibilité et des variantes. Renvoyer une URL d'ici obligerait Food à
    /// connaître le CDN, et chaque changement de domaine à réécrire des tables.
    /// </summary>
    Guid? LogoMediaId,
    Guid? CoverMediaId,

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule, tant que les logos ne sont pas reversés.</summary>
    string? LegacyLogoUrl,
    string Phone,
    string Status,

    /// <summary>
    /// Prend-il une commande MAINTENANT ? Calculé à l'instant de la lecture — le
    /// statut seul ne suffit pas, les horaires et la pause comptent aussi.
    /// </summary>
    bool AcceptsOrdersNow,

    /// <summary>
    /// Pourquoi il n'en prend pas. « Indisponible » sans motif est la réponse la
    /// plus frustrante qui soit : le client ne sait pas s'il doit revenir dans dix
    /// minutes, demain, ou jamais.
    /// </summary>
    string BlockedReason,

    int PreparationMinutes,

    /// <summary>« Manual » ou « Automatic » (§3).</summary>
    string AcceptanceMode,

    /// <summary>Minimum de commande, hors livraison. Nul = aucun.</summary>
    decimal? MinimumOrderAmount,

    /// <summary>
    /// « Normal », « High », « Saturated » (§14).
    ///
    /// CE N'EST PAS UN MOTIF DE BLOCAGE. Un restaurant saturé n'est pas fermé :
    /// il est LENT. Le confondre avec <c>BlockedReason</c> ferait afficher
    /// « revenez demain » à quelqu'un qui aurait très bien pu commander en
    /// acceptant vingt minutes de plus. C'est le « forte demande » du cahier.
    /// </summary>
    string LoadLevel,

    /// <summary>Minutes ajoutées au délai annoncé par la charge actuelle.</summary>
    int ExtraWaitMinutes,

    /// <summary>
    /// Pourquoi l'établissement est exceptionnellement fermé AUJOURD'HUI (§4) :
    /// « Fête de l'Indépendance », « inventaire ». Nul si le jour est ordinaire.
    ///
    /// SEULEMENT LE JOUR COURANT, pas toute la liste. C'est la seule chose
    /// qu'un client a besoin de lire — « fermé » sans raison le fait revenir trois
    /// fois. La liste complète appartient à l'écran du restaurateur.
    /// </summary>
    string? SpecialClosureReason,

    Guid? FulfillmentLocationId,

    /// <summary>
    /// Le dossier vendeur qui encaisse les recettes de l'établissement.
    ///
    /// C'est par lui que passe TOUT le reversement : gains, portefeuille,
    /// retrait, payout. Nul tant qu'aucun dossier n'est rattaché — et
    /// l'établissement ne peut alors pas entrer en service.
    /// </summary>
    Guid? PayoutSellerId,
    IReadOnlyList<ServiceHoursSummary> ServiceHours,

    /// <summary>
    /// A-t-il sa place dans la vitrine ?
    ///
    /// CE N'EST PAS « accepte des commandes ». Un restaurant fermé le soir
    /// reste VISIBLE — le client consulte sa carte et reviendra demain. Un
    /// établissement non validé ou suspendu, lui, ne doit pas exister pour lui.
    ///
    /// Le filtrage se fait chez l'APPELANT, pas ici : cette même API sert
    /// l'espace du restaurateur, qui doit voir son dossier en brouillon, et la
    /// file de validation, qui ne voit que des dossiers en attente.
    /// </summary>
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
/// ═════════════════════════════════════════════════════════════════════════════
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
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record RestaurantCardView(
    Guid Id,
    string Name,
    string? Description,

    /// <summary>Identifiant de média (§6). L'URL se résout hors du module.</summary>
    Guid? LogoMediaId,

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule vers media-service.</summary>
    string? LegacyLogoUrl,

    /// <summary>Le LIEU est-il ouvert ? Ne dit rien de la carte — cf. remarques.</summary>
    bool IsOpenNow,

    /// <summary>Pourquoi il ne l'est pas. « None » quand il l'est.</summary>
    string ClosedReason,

    int PreparationMinutes,
    decimal? MinimumOrderAmount,

    /// <summary>« Normal », « High », « Saturated ». Saturé n'est PAS fermé.</summary>
    string LoadLevel,

    int ExtraWaitMinutes,

    /// <summary>Motif d'une fermeture exceptionnelle AUJOURD'HUI, s'il y en a une.</summary>
    string? SpecialClosureReason);

public interface IFoodModuleApi
{
    Task<RestaurantSummary?> GetRestaurantAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task<RestaurantSummary?> GetRestaurantByOwnerAsync(Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// OÙ CE COMPTE TRAVAILLE-T-IL, ET AVEC QUELS DROITS ?
    ///
    /// C'EST CETTE MÉTHODE QUI REMPLACE <c>GetRestaurantByOwnerAsync</c> POUR
    /// AUTORISER LES ROUTES DE L'ESPACE RESTAURATEUR.
    ///
    /// Tant que le personnel n'existait pas, « le restaurateur » était le compte
    /// qui avait créé l'établissement, et lui seul : un manager, un caissier ou un
    /// cuisinier n'avait accès à RIEN. Résoudre l'établissement par le
    /// propriétaire revenait donc à interdire l'application à tout le personnel du
    /// restaurant.
    ///
    /// Rend <c>null</c> si le compte n'appartient à aucun établissement — y compris
    /// lorsqu'il y a travaillé et en a été retiré.
    /// </summary>
    Task<FoodStaffMembership?> GetStaffMembershipAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// À QUELLE COMMANDE ET À QUEL RESTAURANT CE TICKET SE RATTACHE-T-IL ?
    ///
    /// EXISTE POUR LE RETOUR DE COURSE, ET SEULEMENT POUR LUI.
    ///
    /// Quand HBA Delivery annonce qu'un sac a été remis, il ne connaît qu'une
    /// chaîne de référence — « FOOD-… ». Il faut retrouver la commande commerciale
    /// pour la clore, et donc libérer l'escrow. Sans cette lecture, l'argent du
    /// client resterait immobilisé après un repas déjà mangé.
    ///
    /// Volontairement MAIGRE : ni lignes, ni prix, ni ticket de cuisine. Ce que
    /// l'extérieur a le droit de savoir d'une commande de restaurant se limite à
    /// ses rattachements et à son état.
    /// </summary>
    Task<FoodOrderRef?> GetOrderAsync(Guid foodOrderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN ARTICLE DE CARTE, AVEC SES GROUPES D'OPTIONS ET LEURS ÉCARTS DE PRIX.
    ///
    /// EXISTE POUR QUE LE PANIER CALCULE LE PRIX AU LIEU DE LE RECEVOIR.
    ///
    /// Tant que les repas transitaient par le panier de la marketplace, celui-ci
    /// n'avait aucun client Food : `AddFoodItemToCartCommand` acceptait un
    /// `UnitBaseAmount` venu du corps HTTP, et son propre commentaire admettait
    /// que ni la disponibilité, ni les options, ni le prix n'étaient vérifiés.
    /// Un client pouvait commander à son prix.
    ///
    /// food-cart-service appartient au domaine restauration : il lit la carte,
    /// vérifie que chaque option appartient bien à un groupe de CE plat, que les
    /// groupes obligatoires ont reçu leur choix, et additionne les écarts. Le
    /// montant n'entre plus par la porte du client.
    ///
    /// Rend <c>null</c> si l'article n'existe pas, ou n'appartient pas à ce
    /// restaurant — la même réponse pour les deux, afin de ne pas dire lequel
    /// des deux identifiants était le bon.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<MenuItemView?> GetMenuItemAsync(
        Guid restaurantId, Guid menuItemId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les rattachements d'un ticket de cuisine, vus de l'extérieur du module.
/// </summary>
public sealed record FoodOrderRef(
    Guid FoodOrderId,
    Guid OrderId,
    Guid RestaurantId,
    string Status,

    /// <summary>
    /// De quel univers vient <paramref name="OrderId"/> — voir
    /// <see cref="IntegrationEvents.FoodOrderOrigins"/>.
    ///
    /// SANS LUI, `OrderId` NE DÉSIGNAIT RIEN. `HoldOrderOnDeliveryCancelledHandler`
    /// (order-service) relit ce type après une course `FOOD-` annulée, pour mettre
    /// la commande en arbitrage. Il envoyait donc un identifiant de `MealOrder` à
    /// order-service, qui ne le connaît pas — et la mise en arbitrage d'une
    /// commande de repas n'a jamais fonctionné.
    ///
    /// OPTIONNEL, « Marketplace » PAR DÉFAUT (D32) : les appelants positionnels
    /// existants compilent inchangés, et le défaut décrit exactement les tickets
    /// déjà en base.
    /// </summary>
    string Origin = IntegrationEvents.FoodOrderOrigins.Marketplace);

// ── La carte, telle qu'affichée ─────────────────────────────────────────────

/// <summary>Une option, et son écart de prix.</summary>
public sealed record OptionView(Guid Id, string Name, decimal PriceDelta, bool IsAvailable);

/// <summary>
/// Un groupe de choix, avec ses règles.
///
/// <paramref name="IsRequired"/> est DÉRIVÉ de MinSelections, jamais stocké : un
/// booléen séparé aurait pu contredire le minimum, et il aurait fallu décider
/// lequel des deux ment.
/// </summary>
public sealed record OptionGroupView(
    Guid Id, string Name, int MinSelections, int MaxSelections, bool IsRequired, IReadOnlyList<OptionView> Options);

/// <summary>Un article de la carte.</summary>
public sealed record MenuItemView(
    Guid Id,
    string Name,
    string? Description,
    /// <summary>Identifiant de média (§6). L'URL se résout hors du module.</summary>
    Guid? ImageMediaId,

    /// <summary>TRANSITOIRE : l'URL d'avant la bascule.</summary>
    string? LegacyImageUrl,

    /// <summary>
    /// L'adresse à afficher : `ImagePublicUrl` si le média est repris,
    /// `LegacyImageUrl` sinon. Nulle quand l'article n'a pas de photo.
    /// </summary>
    /// <remarks>
    /// LE REPLI EST FAIT ICI, PAS DANS LES TROIS APPLICATIONS.
    ///
    /// Client, vendeur et livreur afficheraient tous les trois le même
    /// `imagePublicUrl ?? legacyImageUrl` — et le jour où `LegacyImageUrl` disparaît,
    /// il faudrait trois publications de boutique pour le retirer. Les deux champs
    /// bruts restent exposés pour qui doit distinguer un média repris d'un média
    /// hérité ; celui-ci sert à AFFICHER.
    /// </remarks>
    string? DisplayImageUrl,
    decimal BasePrice,
    string Currency,

    /// <summary>
    /// Commandable MAINTENANT : PHOTO PRÉSENTE, disponible, et tous les groupes
    /// obligatoires satisfiables.
    /// </summary>
    bool IsOrderable,

    /// <summary>
    /// L'article porte-t-il une photo ?
    /// </summary>
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
    bool HasImage,

    /// <summary>
    /// Quand il revient, si c'est connu. « De retour demain » vaut mieux
    /// qu'« indisponible », qui ne dit pas s'il faut revenir dans dix minutes ou
    /// jamais.
    /// </summary>
    DateTime? BackAtUtc,

    IReadOnlyList<OptionGroupView> OptionGroups);

/// <summary>Une section de carte : « Entrées », « Plats », « Boissons ».</summary>
public sealed record MenuSectionView(
    Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<MenuItemView> Items);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE CARTE : « Menu du midi », « Carte du soir », « Carte d'été ».
///
/// NIVEAU NOUVEAU. La réponse ne portait qu'une liste de sections ; elle porte
/// désormais des CARTES qui portent les sections (cahier §5).
///
/// <paramref name="IsServedNow"/> ET <paramref name="IsActive"/> DISENT DEUX
/// CHOSES DIFFÉRENTES, et l'écran doit les traiter différemment :
///
///   • « inactive » est une décision du restaurateur, qui dure jusqu'à ce qu'il
///     la reprenne — la carte d'été remisée en novembre ;
///   • « pas servie maintenant » se lève tout seul à 11 h le lendemain.
///
/// Les confondre ferait afficher « carte désactivée » à un client de 20 h devant
/// un menu du midi parfaitement actif.
///
/// Les bornes sont rendues en CHAÎNES formatées — « 11:00 », « 2026-06-01 » — et
/// non en types date : elles s'affichent, elles ne se calculent pas côté client,
/// et un fuseau appliqué deux fois décalerait le menu du midi d'une heure.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

/// <summary>
/// L'établissement du compte connecté, et son rôle dedans.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE CONTRAT MANQUAIT, ET IL BLOQUAIT LE SÉLECTEUR D'ACTIVITÉ.
///
/// HBA Partner doit afficher, au démarrage, TOUT ce que le compte gère :
/// boutiques et restaurants. Les boutiques se lisent par
/// <c>GET /api/merchants/{sellerId}/stores</c>. Les restaurants, eux, n'avaient
/// aucune route : <c>GetStaffMembershipAsync</c> existait en interne, mais rien
/// ne l'exposait — et la fiche publique refuse un établissement en brouillon,
/// c'est-à-dire précisément celui qu'un nouveau restaurateur doit voir.
///
/// UN COMPTE = AU PLUS UN ÉTABLISSEMENT, AUJOURD'HUI.
///
/// <c>GetStaffMembershipAsync</c> rend une appartenance unique. Le jour où un
/// même compte travaillera dans deux restaurants, ce contrat devra rendre une
/// liste — et le sélecteur d'activité de l'application le suppose déjà.
///
/// NI CHIFFRE D'AFFAIRES, NI DOSSIER DE REVERSEMENT AU-DELÀ DE L'IDENTIFIANT.
///
/// Ce contrat sert à CHOISIR une activité, pas à la piloter. Le tableau de bord
/// a ses propres lectures, et un caissier qui n'a pas la permission des finances
/// ne doit pas apprendre le chiffre du jour en ouvrant l'application.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed record PartnerRestaurantView(
    Guid RestaurantId,
    string Name,
    string Status,

    /// <summary>« Owner », « Manager », « Cashier », « Cook »…</summary>
    string Role,

    bool IsFounder,
    bool IsActive,

    /// <summary>Permissions effectives, dérogations comprises.</summary>
    IReadOnlyList<string> Permissions,

    /// <summary>
    /// Le dossier vendeur qui encaisse. Nul tant qu'aucun n'est rattaché — et
    /// l'établissement ne peut alors pas entrer en service.
    /// </summary>
    Guid? PayoutSellerId,

    bool AcceptsOrdersNow,
    string BlockedReason);
