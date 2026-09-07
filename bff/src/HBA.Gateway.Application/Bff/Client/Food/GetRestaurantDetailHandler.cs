using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>Fiche d'un restaurant (§9).</summary>
public sealed class GetRestaurantDetailHandler
{
    public const string ScreenId = "client.food.restaurant_detail";

    private readonly IFoodClient _food;

    public GetRestaurantDetailHandler(IFoodClient food) => _food = food;

    public async Task<BffEnvelope<FoodRestaurantDetailDto>> HandleAsync(
        Guid restaurantId, CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        var detailTask = context.CallAsync(
            "Food", () => _food.GetRestaurantAsync(restaurantId, cancellationToken));

        var menuTask = context.CallAsync(
            "Food", () => _food.GetMenuAsync(restaurantId, cancellationToken));

        await Task.WhenAll(detailTask, menuTask);

        // Un 404 sur la fiche remonte en `BffResourceNotFoundException` : le
        // service rend déjà 404 pour un établissement hors vitrine, sans le
        // distinguer d'un identifiant inexistant.
        var detail = context.Resolve(
            DependencyCriticality.Critical, "Food", await detailTask)!;

        var menu = context.Resolve(
            DependencyCriticality.Important, "Food", await menuTask);

        var dto = new FoodRestaurantDetailDto(
            Restaurant: new FoodRestaurantHeaderDto(
                detail.Id,
                detail.Name,
                detail.Description,
                detail.LogoMediaId,
                detail.LegacyLogoUrl,
                detail.CoverMediaId,
                detail.Phone,
                detail.AcceptsOrdersNow,
                detail.BlockedReason,
                detail.PreparationMinutes,
                detail.MinimumOrderAmount,
                detail.LoadLevel,
                detail.ExtraWaitMinutes,
                detail.SpecialClosureReason,
                [
                    .. detail.ServiceHours.Select(hours => new FoodServiceHoursDto(
                        hours.Day, hours.OpensAt, hours.ClosesAt)),
                ]),
            Rating: null,
            Delivery: FoodDeliveryDto.NotEvaluated,
            Menus:
            [
                .. (menu?.Menus ?? [])
                    // LES CARTES INACTIVES SONT ÉCARTÉES, PAS CELLES HORS CRÉNEAU.
                    .Where(m => m.IsActive)
                    .Select(m => new FoodMenuDto(
                        m.Id,
                        m.Name,
                        m.Description,
                        m.IsServedNow,
                        m.ServedFrom,
                        m.ServedUntil,
                        [
                            .. m.Sections
                                .Where(section => section.IsActive)
                                .Select(section => new FoodMenuSectionDto(
                                    section.Id,
                                    section.Name,
                                    section.Description,
                                    [
                                        .. section.Items.Select(item => new FoodMenuItemDto(
                                            item.Id,
                                            item.Name,
                                            item.Description,
                                            item.ImageMediaId,
                                            item.LegacyImageUrl,
                                            item.BasePrice,
                                            item.Currency,
                                            item.IsOrderable,
                                            item.BackAtUtc)),
                                    ])),
                        ])),
            ],
            PopularItems: []);

        return context.Complete(dto);
    }
}
