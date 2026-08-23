using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Menus;

/// <summary>
/// Le prix d'un plat avec les options retenues, et de quoi l'afficher.
///
/// LES LIBELLÉS SONT RENVOYÉS MAIS NE DOIVENT PAS ÊTRE STOCKÉS.
///
/// Ils servent à confirmer le choix à l'écran (« Riz au gras — Grande taille,
/// poulet »). Les figer dans le panier ferait afficher pendant des semaines un
/// nom que le restaurateur a corrigé depuis.
/// </summary>
public sealed record MenuItemQuote(
    Guid MenuItemId,
    string Name,
    decimal UnitPrice,
    string Currency,
    IReadOnlyList<QuotedOption> Options);

public sealed record QuotedOption(Guid OptionGroupId, Guid OptionId, string GroupName, string OptionName, decimal PriceDelta);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// COMBIEN COÛTE CE PLAT AVEC CES OPTIONS, ET PEUT-ON LE COMMANDER ?
///
/// CETTE REQUÊTE EXISTE POUR QUE LE PANIER N'AIT PAS À SAVOIR.
///
/// Ajouter un plat au panier suppose quatre vérifications que seul Food peut
/// faire : l'établissement prend-il des commandes à cet instant, le plat est-il
/// disponible, les options appartiennent-elles à ce plat, et les groupes
/// obligatoires ont-ils reçu leur choix. Les recopier côté Cart y importerait le
/// vocabulaire de la restauration — et la copie divergerait à la première règle
/// modifiée.
///
/// CE PRIX N'ENGAGE PAS. Il est recalculé à la réception de la commande, à
/// partir de la même carte. Un plat dont le prix change entre l'ajout au panier
/// et le paiement est facturé au prix de la carte : c'est le restaurant qui fixe
/// ses prix, pas l'instantané que le client garde ouvert sur son téléphone.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

        // Même réponse que pour un identifiant inconnu : distinguer les deux
        // dirait à qui teste des identifiants lesquels existent.
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return Result.Failure<MenuItemQuote>(
                Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        // L'ÉTABLISSEMENT DOIT PRENDRE DES COMMANDES MAINTENANT.
        //
        // Sans ce contrôle, on remplirait le panier d'un restaurant fermé, et le
        // refus n'arriverait qu'au paiement — après que le client a saisi son
        // adresse et choisi son moyen de paiement.
        var blocage = restaurant.CanAcceptOrders(maintenant);
        if (blocage != OrderingBlockedReason.None)
        {
            return Result.Failure<MenuItemQuote>(Error.Conflict(
                "food.restaurant.not_accepting",
                "Cet établissement ne prend pas de commandes actuellement."));
        }

        var item = await _items.GetByIdAsync(new MenuItemId(query.MenuItemId), cancellationToken);

        // LE PLAT DOIT APPARTENIR À CE RESTAURANT.
        //
        // Sans cette comparaison, un client passerait l'identifiant d'un plat d'un
        // autre établissement : le prix serait celui de l'autre carte, et la
        // commande partirait vers une cuisine qui ne connaît pas ce plat.
        if (item is null || item.RestaurantId != query.RestaurantId)
        {
            return Result.Failure<MenuItemQuote>(
                Error.NotFound("food.item.not_found", "Article introuvable."));
        }

        // LA CARTE DU PLAT DOIT ÊTRE SERVIE À CETTE HEURE.
        //
        // `PriceSelection` juge l'article et ses options, jamais le CRÉNEAU de la
        // carte qui le contient. Sans ce contrôle, le menu du midi resterait
        // commandable à 22 h — c'est précisément le cas que le second niveau de la
        // carte existe pour représenter.
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
