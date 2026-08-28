namespace HBA.Gateway.Application.Contracts.Food;

/// <summary>
/// Une carte de la vitrine — miroir de <c>RestaurantCardView</c>.
/// </summary>
/// <remarks>
/// `IsOpenNow` N'EST PAS `AcceptsOrdersNow`, ET LE NOM PORTE LA PROMESSE.
///
/// La liste ne vérifie que le LIEU : ouvert, pas en pause, pas fermé
/// exceptionnellement. Elle ne vérifie PAS qu'un plat est commandable — cela
/// coûterait jusqu'à quatre requêtes par établissement, soit quatre-vingts pour
/// une page de vingt. La fiche, elle, rend la réponse ferme.
///
/// Renommer ce champ en `AcceptsOrdersNow` ferait traverser la ville à un client
/// pour découvrir que tout est épuisé.
/// </remarks>
public sealed record RestaurantCard(
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

/// <summary>
/// Fiche d'un établissement — miroir PARTIEL de <c>RestaurantSummary</c>.
/// </summary>
/// <remarks>
/// CHAMPS DE GESTION OMIS VOLONTAIREMENT.
///
/// `OwnerUserId`, `PayoutSellerId` et `FulfillmentLocationId` sont dans le
/// contrat amont parce qu'il sert AUSSI l'espace du restaurateur et la file de
/// validation. Un client n'a rien à faire du compte propriétaire ni du dossier
/// qui encaisse : les transporter jusqu'à un téléphone serait une divulgation
/// gratuite.
///
/// Ce que la désérialisation ignore ne coûte rien.
/// </remarks>
/// <param name="AcceptsOrdersNow">
/// <summary>Réponse FERME : lieu ouvert ET au moins un plat commandable.</summary>
/// </param>
public sealed record RestaurantDetail(
    Guid Id,
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
    IReadOnlyList<RestaurantServiceHours> ServiceHours,
    bool IsPubliclyVisible);

public sealed record RestaurantServiceHours(string Day, string OpensAt, string ClosesAt);

/// <summary>Carte d'un restaurant — miroir de <c>RestaurantMenuView</c>.</summary>
/// <remarks>
/// TROIS NIVEAUX, ET AUCUN N'EST DÉCORATIF.
///
/// Carte → section → plat. La carte porte un CRÉNEAU (<c>IsServedNow</c>) : à
/// 20 h, le menu du midi et tous ses plats ne comptent pour rien, même
/// disponibles. Aplatir les trois niveaux en une liste de plats ferait
/// disparaître ce créneau, et proposerait un plat du midi le soir.
/// </remarks>
public sealed record RestaurantMenu(
    Guid RestaurantId,
    string Name,
    bool AcceptsOrdersNow,
    string BlockedReason,
    int PreparationMinutes,
    IReadOnlyList<FoodMenu> Menus);

public sealed record FoodMenu(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    bool IsServedNow,
    string? ServedFrom,
    string? ServedUntil,
    IReadOnlyList<FoodMenuSection> Sections);

public sealed record FoodMenuSection(
    Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<FoodMenuItem> Items);

/// <summary>
/// Un plat.
/// </summary>
/// <remarks>
/// `IsOrderable` EST UNE RÉPONSE FERME, ET LES GROUPES D'OPTIONS SONT OMIS.
///
/// Le contrat amont porte les groupes d'options complets — obligatoires,
/// suppléments, prix delta. Une carte parcourue n'en affiche aucun : ils
/// n'apparaissent qu'au moment d'ajouter le plat au panier. Les transporter pour
/// chaque plat d'une carte de soixante entrées multiplierait la réponse par
/// quatre, sur un réseau où l'octet se paie.
///
/// Ils reviendront sur l'écran d'ajout, qui interroge UN plat.
/// </remarks>
public sealed record FoodMenuItem(
    Guid Id,
    string Name,
    string? Description,
    Guid? ImageMediaId,
    string? LegacyImageUrl,
    decimal BasePrice,
    string Currency,
    bool IsOrderable,
    DateTime? BackAtUtc);

/// <summary>L'établissement du compte connecté — miroir de <c>PartnerRestaurantView</c>.</summary>
public sealed record PartnerRestaurant(
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

/// <summary>Tableau de cuisine — miroir de <c>KitchenBoardView</c>.</summary>
public sealed record KitchenBoard(
    Guid RestaurantId,
    Guid? StationId,
    IReadOnlyList<KitchenStation> Stations,
    IReadOnlyList<KitchenTicket> Tickets);

public sealed record KitchenStation(Guid Id, string Name, bool IsActive);

public sealed record KitchenTicket(
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
    IReadOnlyList<KitchenTicketItem> Items);

public sealed record KitchenTicketItem(
    Guid Id,
    string Name,
    int Quantity,
    string? Notes,
    string Status,
    Guid? PreparationStationId,
    int PreparationMinutes,
    IReadOnlyList<string> Options);
