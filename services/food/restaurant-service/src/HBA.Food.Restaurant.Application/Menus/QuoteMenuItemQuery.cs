using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Menus;

/// <summary>Le prix d'un plat avec les options retenues, et de quoi l'afficher.</summary>
public sealed record MenuItemQuote(
    Guid MenuItemId,
    string Name,
    decimal UnitPrice,
    string Currency,
    IReadOnlyList<QuotedOption> Options);

public sealed record QuotedOption(Guid OptionGroupId, Guid OptionId, string GroupName, string OptionName, decimal PriceDelta);

/// <summary>COMBIEN COÛTE CE PLAT AVEC CES OPTIONS, ET PEUT-ON LE COMMANDER ?</summary>
public sealed record QuoteMenuItemQuery(
    Guid RestaurantId,
    Guid MenuItemId,
    IReadOnlyList<Guid> SelectedOptionIds) : IQuery<MenuItemQuote>;

internal sealed class QuoteMenuItemQueryHandler : IQueryHandler<QuoteMenuItemQuery, MenuItemQuote>
{
    private readonly IRestaurantRepository _restaurants;
    private readonly IMenuItemRepository _items;
    private readonly IMenuRepository _menus;
    private readonly IMenuCategoryRepository _categories;

    public QuoteMenuItemQueryHandler(
        IRestaurantRepository restaurants,
        IMenuItemRepository items,
        IMenuRepository menus,
        IMenuCategoryRepository categories)
    {
        _restaurants = restaurants;
        _items = items;
        _menus = menus;
        _categories = categories;
    }

    public async Task<Result<MenuItemQuote>> Handle(QuoteMenuItemQuery query, CancellationToken cancellationToken)
    {
        // L'heure est lue UNE fois et passée partout : la relire par contrôle
        // ferait qu'à 15 h 00 pile la carte du midi pourrait se fermer entre le
        // moment où l'on juge le plat disponible et celui où l'on juge sa carte
        // servie — et le client recevrait un refus qu'aucune règle n'explique.
        var maintenant = DateTime.UtcNow;

        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(query.RestaurantId), cancellationToken);

        // Même réponse que pour un identifiant inconnu : distinguer les deux dirait
        // à qui teste des identifiants lesquels existent.
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return Result.Failure<MenuItemQuote>(
                Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        // L'ÉTABLISSEMENT DOIT PRENDRE DES COMMANDES MAINTENANT.
        var blocage = restaurant.CanAcceptOrders(maintenant);
        if (blocage != OrderingBlockedReason.None)
        {
            return Result.Failure<MenuItemQuote>(Error.Conflict(
                "food.restaurant.not_accepting",
                "Cet établissement ne prend pas de commandes actuellement."));
        }

        var item = await _items.GetByIdAsync(new MenuItemId(query.MenuItemId), cancellationToken);

        // LE PLAT DOIT APPARTENIR À CE RESTAURANT.
        if (item is null || item.RestaurantId != query.RestaurantId)
        {
            return Result.Failure<MenuItemQuote>(
                Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        // LA CARTE DU PLAT DOIT ÊTRE SERVIE À CETTE HEURE.
        var section = (await _categories.ListByRestaurantAsync(query.RestaurantId, cancellationToken))
            .FirstOrDefault(c => c.Id.Value == item.MenuCategoryId);

        var carte = section is null
            ? null
            : (await _menus.ListByRestaurantAsync(query.RestaurantId, cancellationToken))
                .FirstOrDefault(m => m.Id.Value == section.MenuId);

        if (section is null || !section.IsActive || carte is null || !carte.IsServedAt(maintenant))
        {
            return Result.Failure<MenuItemQuote>(Error.Conflict(
                "food.item.not_served",
                "Cet article n'est pas servi actuellement."));
        }

        var prix = item.PriceSelection(query.SelectedOptionIds, maintenant);
        if (prix.IsFailure)
        {
            return Result.Failure<MenuItemQuote>(prix.Error);
        }

        // Le groupe de chaque option est retrouvé ici : `SelectedOption` porte le
        // NOM du groupe, pas son identifiant, et c'est l'identifiant que le panier
        // enregistre pour permettre à la cuisine de regrouper l'affichage.
        var groupeParOption = item.OptionGroups
            .SelectMany(g => g.Options.Select(o => (o.Id, GroupId: g.Id)))
            .ToDictionary(x => x.Id, x => x.GroupId);

        return Result.Success(new MenuItemQuote(
            item.Id.Value,
            item.Name,
            prix.Value.UnitPrice,
            prix.Value.Currency,
            prix.Value.Options
                .Select(o => new QuotedOption(
                    groupeParOption[o.OptionId], o.OptionId, o.GroupName, o.OptionName, o.PriceDelta))
                .ToList()));
    }
}
