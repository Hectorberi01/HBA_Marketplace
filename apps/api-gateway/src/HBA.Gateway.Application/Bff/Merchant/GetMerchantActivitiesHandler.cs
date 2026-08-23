using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Merchant;

/// <summary>
/// Le sélecteur d'activité de HBA Partner (§11, §44).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CRITICITÉ (§23) — ET AUCUNE DÉPENDANCE N'EST CRITIQUE. C'EST VOULU.
///
///   Merchant · vendeur    IMPORTANTE — pas de boutiques, mais l'écran vit.
///   Merchant · boutiques  IMPORTANTE
///   Food · établissement  OPTIONNELLE — 404 = « ce compte n'a pas de restaurant »,
///                                       ce qui est le cas de la majorité.
///
/// UN ÉCRAN VIDE VAUT MIEUX QU'UNE ERREUR, ICI PLUS QU'AILLEURS.
///
/// C'est le PREMIER écran après la connexion. Le faire échouer parce qu'un
/// service tarde enferme le partenaire dehors : il ne peut ni consulter, ni
/// comprendre, ni réessayer utilement. Une liste vide accompagnée d'un
/// avertissement lui laisse au moins l'application.
///
/// UN 404 SUR L'ÉTABLISSEMENT N'EST PAS UNE DÉGRADATION.
///
/// food-service répond 404 quand le compte ne travaille nulle part — le cas
/// normal d'un vendeur qui n'a que des boutiques. Le compter comme un incident
/// remplirait le tableau d'avertissements à chaque connexion de chaque
/// commerçant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetMerchantActivitiesHandler
{
    public const string ScreenId = "merchant.activities";

    public const string StoreType = "STORE";
    public const string RestaurantType = "RESTAURANT";

    private readonly IMerchantClient _merchant;
    private readonly IFoodClient _food;

    public GetMerchantActivitiesHandler(IMerchantClient merchant, IFoodClient food)
    {
        _merchant = merchant;
        _food = food;
    }

    public async Task<BffEnvelope<MerchantActivitiesDto>> HandleAsync(
        CancellationToken cancellationToken)
    {
        using var context = AggregationContext.Start(ScreenId);

        // ── Vague 1 : le dossier vendeur et l'établissement, en parallèle ────
        //
        // L'établissement ne dépend que du jeton : rien n'oblige à attendre le
        // vendeur pour le demander.
        var sellerTask = context.CallAsync(
            "Merchant", () => _merchant.GetMySellerAsync(cancellationToken));

        var restaurantTask = context.CallAsync(
            "Food", () => _food.GetMyRestaurantAsync(cancellationToken));

        await Task.WhenAll(sellerTask, restaurantTask);

        var seller = context.Resolve(
            DependencyCriticality.Important, "Merchant", await sellerTask);

        var restaurant = context.Resolve(
            DependencyCriticality.Optional, "Food", await restaurantTask);

        // ── Vague 2 : les boutiques, qui exigent le sellerId de la vague 1 ───
        var stores = seller is null
            ? null
            : context.Resolve(
                DependencyCriticality.Important,
                "Merchant",
                await context.CallAsync(
                    "Merchant", () => _merchant.ListStoresAsync(seller.Id, cancellationToken)));

        var activities = new List<MerchantActivityDto>();

        foreach (var store in stores ?? [])
        {
            activities.Add(new MerchantActivityDto(
                Type: StoreType,
                Id: store.Id,
                Name: store.Name,
                LogoUrl: store.LogoUrl,
                // Déduit : merchant-service n'a aucun modèle de personnel.
                // Cf. `MerchantActivityDto.Role`.
                Role: "OWNER",
                Status: store.Status,
                IsOpenNow: store.IsSelling));
        }

        if (restaurant is not null)
        {
            activities.Add(new MerchantActivityDto(
                Type: RestaurantType,
                Id: restaurant.RestaurantId,
                Name: restaurant.Name,
                // food-service rend un identifiant de média, pas une URL — et la
                // passerelle ne les résout pas encore (cf. `FoodRestaurantCardDto`).
                LogoUrl: null,
                // Le rôle RÉEL, lui : Food a un modèle de personnel complet.
                Role: restaurant.Role.ToUpperInvariant(),
                Status: restaurant.Status,
                IsOpenNow: restaurant.AcceptsOrdersNow));
        }

        return context.Complete(new MerchantActivitiesDto(activities));
    }
}
