using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>
/// Fiche d'un restaurant (§9).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRITICITÉ (§23)
///
///   Food · fiche  CRITIQUE    — sans établissement, il n'y a pas d'écran.
///   Food · carte  IMPORTANTE  — la fiche s'affiche sans carte, avec avertissement.
///
/// DEUX APPELS AU MÊME SERVICE, EN PARALLÈLE.
///
/// Fiche et carte sont deux routes distinctes de food-service. Les enchaîner
/// doublerait la latence perçue pour rien : la carte ne dépend pas de la fiche,
/// seul l'identifiant leur est commun — et il vient de l'URL.
///
/// NI NOTE, NI TARIF DE LIVRAISON, NI PLATS POPULAIRES.
///
/// Le §9 les prévoit tous les trois. Aucun n'a d'amont : engagement-service ne
/// note que des produits et des vendeurs, jamais un établissement ; un devis de
/// livraison exige une adresse de destination que la fiche ne connaît pas ; et
/// aucun service ne compte les ventes par plat.
///
/// Les trois champs sont donc présents et vides — le contrat client reste stable
/// — mais AUCUN n'émet d'avertissement : une absence permanente n'est pas une
/// dégradation, et le tableau d'avertissements ne doit signaler que ce qui peut
/// se rétablir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
                    //
                    // Une carte inactive n'existe pas pour le client. Une carte
                    // active mais hors créneau — le menu du midi, consulté le soir
                    // — reste visible : le client la parcourt et reviendra demain.
                    // La masquer viderait l'écran à 20 h.
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
