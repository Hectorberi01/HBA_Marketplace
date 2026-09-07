namespace HBA.Gateway.Application.Contracts.Food;

/// <summary>Une carte de la vitrine — miroir de <c>RestaurantCardView</c>.</summary>
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

/// <summary>Fiche d'un établissement — miroir PARTIEL de <c>RestaurantSummary</c>.</summary>
/// <param name="AcceptsOrdersNow">
/// <summary> Réponse FERME : lieu ouvert ET au moins un plat commandable.</summary>
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

/// <summary>Un plat.</summary>
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
