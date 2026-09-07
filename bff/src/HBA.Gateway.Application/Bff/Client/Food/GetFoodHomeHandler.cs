using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>
/// Accueil HBA Food.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRITICITÉ (§23)
///
///   Food    CRITIQUE    — sans vitrine, l'écran n'a aucun contenu.
///   Order   OPTIONNELLE — bandeau « commande en cours », absent hors session.
///
/// DEUX DÉPENDANCES SEULEMENT, ET C'EST LE REFLET DE CE QUI EXISTE.
///
/// Le §8 en prévoit quatre — Food, Engagement, Delivery, Order. Engagement n'a
/// aucune note d'établissement, et Delivery ne peut pas chiffrer une course sans
/// adresse de destination. Les appeler pour n'en rien tirer coûterait deux
/// allers-retours par affichage de l'écran le plus consulté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>
/// Les statuts de commande considérés comme « en cours ».
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PARTAGÉ AVEC L'ACCUEIL EXPRESS, ET C'ÉTAIT DUPLIQUÉ.
///
/// La liste vivait dans <c>GetExpressHomeHandler</c>. La recopier ici aurait
/// garanti la divergence : un statut ajouté d'un seul côté ferait disparaître le
/// bandeau « commande en cours » dans un univers et pas dans l'autre — un défaut
/// que personne ne signale, parce qu'il ressemble à « je n'ai pas de commande ».
///
/// LISTE ÉTABLIE PAR DÉFAUT, À CONFIRMER AVEC order-service.
///
/// Le service n'expose aucun filtre de statut : la sélection se fait ici, sur des
/// valeurs relevées dans son domaine sans garantie d'exhaustivité.
///
/// NE DISTINGUE PAS UNE COMMANDE FOOD D'UNE COMMANDE MARCHANDISE.
///
/// La nature vit sur les LIGNES (<c>OrderLineSummary.Kind</c> : « Goods » ou
/// « Food »), que le résumé de commande ne transporte pas. Les deux accueils
/// montreront donc la même commande en cours. À corriger dès que le service rend
/// la nature au niveau de la commande — ou que le résumé porte ses lignes.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class FoodOrderStatuses
{
    private static readonly string[] Active =
    [
        "Pending", "AwaitingPayment", "Paid", "Confirmed", "Preparing", "Shipped", "InTransit",
    ];

    public static bool IsActive(string status)
        => Active.Contains(status, StringComparer.OrdinalIgnoreCase);
}
