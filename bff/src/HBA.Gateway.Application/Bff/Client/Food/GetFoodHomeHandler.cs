using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>Accueil HBA Food.</summary>
public sealed class GetFoodHomeHandler
{
    public const string ScreenId = "client.food.home";

    private readonly IFoodClient _food;
    private readonly IOrderClient _order;

    public GetFoodHomeHandler(IFoodClient food, IOrderClient order)
    {
        _food = food;
        _order = order;
    }

    public async Task<BffEnvelope<FoodHomeDto>> HandleAsync(
        PageRequest page, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var storefrontTask = context.CallAsync(
            "Food", () => _food.ListStorefrontAsync(page.Page, page.PageSize, cancellationToken));

        var ordersTask = context.CallAsync(
            "Order", () => _order.ListMineAsync(cancellationToken));

        await Task.WhenAll(storefrontTask, ordersTask);

        var storefront = context.Resolve(
            DependencyCriticality.Critical, "Food", await storefrontTask)!;

        var orders = context.Resolve(
            DependencyCriticality.Optional, "Order", await ordersTask);

        var activeOrder = orders?
            .Where(order => FoodOrderStatuses.IsActive(order.Status))
            .OrderByDescending(order => order.CreatedAtUtc)
            .Select(order => new FoodActiveOrderDto(
                order.Id, order.Status, order.GrandTotal, order.Currency))
            .FirstOrDefault();

        var dto = new FoodHomeDto(
            Restaurants: PagedResult<FoodRestaurantCardDto>.Of(
                [
                    .. storefront.Select(card => new FoodRestaurantCardDto(
                        card.Id,
                        card.Name,
                        card.Description,
                        card.LogoMediaId,
                        card.LegacyLogoUrl,
                        card.IsOpenNow,
                        card.ClosedReason,
                        card.PreparationMinutes,
                        card.MinimumOrderAmount,
                        card.LoadLevel,
                        card.ExtraWaitMinutes,
                        card.SpecialClosureReason)),
                ],
                page),
            ActiveOrder: activeOrder,
            Cuisines: [],
            DeliveryOffers: []);

        return context.Complete(dto);
    }
}

/// <summary>Les statuts de commande considérés comme « en cours ».</summary>
public static class FoodOrderStatuses
{
    private static readonly string[] Active =
    [
        "Pending", "AwaitingPayment", "Paid", "Confirmed", "Preparing", "Shipped", "InTransit",
    ];

    public static bool IsActive(string status)
        => Active.Contains(status, StringComparer.OrdinalIgnoreCase);
}
