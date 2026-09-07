namespace HBA.Gateway.Application.Bff.Restaurant;

/// <summary>Tableau de bord d'un restaurant (§13).</summary>
public sealed record RestaurantDashboardDto(
    RestaurantHeaderDto Restaurant,
    RestaurantServiceDto Service,
    RestaurantWalletDto? Wallet,
    RestaurantKitchenSummaryDto Kitchen);

public sealed record RestaurantHeaderDto(
    Guid Id,
    string Name,
    string Status,
    string Role,
    IReadOnlyList<string> Permissions);

/// <summary>L'état du service, tel qu'un restaurateur le lit d'un coup d'œil.</summary>
public sealed record RestaurantServiceDto(bool AcceptsOrdersNow, string BlockedReason);

public sealed record RestaurantWalletDto(
    decimal PendingBalance,
    decimal AvailableBalance,
    decimal PendingWithdrawal,
    string Currency);

/// <summary>Ce qui attend en cuisine, en trois nombres.</summary>
public sealed record RestaurantKitchenSummaryDto(int Pending, int Preparing, int Ready);

/// <summary>Écran de cuisine — KDS (§14).</summary>
public sealed record RestaurantKitchenDto(
    Guid RestaurantId,
    Guid? StationId,
    IReadOnlyList<KitchenStationDto> Stations,
    IReadOnlyList<KitchenTicketDto> Pending,
    IReadOnlyList<KitchenTicketDto> Preparing,
    IReadOnlyList<KitchenTicketDto> Ready);

public sealed record KitchenStationDto(Guid Id, string Name, bool IsActive);

/// <param name="ElapsedSeconds">Temps écoulé depuis la réception.</param>
public sealed record KitchenTicketDto(
    Guid FoodOrderId,
    Guid OrderId,
    string Status,
    int Priority,
    int? EstimatedPreparationMinutes,
    DateTime ReceivedAtUtc,
    int ElapsedSeconds,
    string? CustomerNote,
    int OtherStationsPending,
    IReadOnlyList<KitchenTicketItemDto> Items);

public sealed record KitchenTicketItemDto(
    Guid Id,
    string Name,
    int Quantity,
    string? Notes,
    string Status,
    Guid? PreparationStationId,
    int PreparationMinutes,
    IReadOnlyList<string> Options);
