using HBA.Food.Contracts;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Menus;

/// <summary>
/// La carte d'un restaurant.
///
/// DEUX PUBLICS, DEUX RÉPONSES — d'où le drapeau.
///
/// Le CLIENT ne doit voir que ce qu'il peut commander : lui afficher un plat
/// épuisé, c'est le faire choisir puis refuser son panier. Le RESTAURATEUR doit
/// voir toute sa carte, épuisés compris — sinon il ne peut pas remettre en vente
/// ce qu'il ne voit plus.
///
/// Servir la même réponse aux deux forcerait l'un des deux écrans à mentir.
/// </summary>
public enum MenuAudience
{
    /// <summary>Un client. Ne voit que ce qu'il peut commander, et seulement d'un établissement EN SERVICE.</summary>
    Public = 0,

    /// <summary>Le restaurateur chez lui. Voit tout : cartes hors créneau, sections masquées, plats épuisés.</summary>
    Owner = 1
}

public sealed record GetMenuQuery(Guid RestaurantId, MenuAudience Audience) : IQuery<RestaurantMenuView>;

internal sealed class MenuQueryHandler : IQueryHandler<GetMenuQuery, RestaurantMenuView>
{
    private readonly IRestaurantRepository _restaurants;
    private readonly IMenuRepository _menus;
    private readonly IMenuCategoryRepository _categories;
    private readonly IMenuItemRepository _items;

    public MenuQueryHandler(
        IRestaurantRepository restaurants,
        IMenuRepository menus,
        IMenuCategoryRepository categories,
        IMenuItemRepository items)
    {
        _restaurants = restaurants;
        _menus = menus;
        _categories = categories;
        _items = items;
    }

    public async Task<Result<RestaurantMenuView>> Handle(GetMenuQuery query, CancellationToken cancellationToken)
    {
        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(query.RestaurantId), cancellationToken);

        // UN ÉTABLISSEMENT NON VALIDÉ OU SUSPENDU N'EXISTE PAS POUR UN CLIENT.
        //
        // La première version rendait la carte de n'importe quel identifiant. Un
        // dossier en brouillon — jamais examiné — était consultable, et une
        // suspension ne retirait rien de la vitrine.
        //
        // Même réponse que pour un identifiant inconnu : distinguer les deux
        // dirait à qui teste des identifiants lesquels existent.
        if (restaurant is null
            || (query.Audience == MenuAudience.Public && !restaurant.IsPubliclyVisible))
        {
            return Result.Failure<RestaurantMenuView>(
                Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        // L'HEURE EST LUE ICI, ET PASSÉE PARTOUT ENSUITE.
        //
        // Un seul instant pour toute la projection. La relire par niveau ferait
        // qu'à 15 h 00 pile la carte du midi pourrait se fermer entre le moment où
        // l'on décide de l'afficher et celui où l'on compte ses plats — et la
        // réponse annoncerait une carte vide sans dire pourquoi.
        var maintenant = DateTime.UtcNow;
        var toutVoir = query.Audience == MenuAudience.Owner;

        // UN PLAT NE PEUT PAS ÊTRE « COMMANDABLE » DANS UN RESTAURANT QUI NE
        // PREND RIEN.
        //
        // IsOrderableAt ne regarde que l'article : disponible, groupes
        // satisfiables. Il ignore l'établissement. Sans cette variable, la carte
        // d'un restaurant fermé ou suspendu annonçait chaque plat comme
        // commandable — le client choisissait, et le panier refusait.
        //
        // SURCHARGE À UN SEUL PARAMÈTRE, ET IL LE FAUT : la version complète
        // demande s'il reste un article commandable, réponse qui se construit à
        // partir de cette variable même. La passer ici serait circulaire.
        var etablissementSert = restaurant.CanAcceptOrders(maintenant) == OrderingBlockedReason.None;

        var cartes = await _menus.ListByRestaurantAsync(query.RestaurantId, cancellationToken);
        var sections = await _categories.ListByRestaurantAsync(query.RestaurantId, cancellationToken);
        var articles = await _items.ListByRestaurantAsync(query.RestaurantId, cancellationToken);

        var vues = new List<MenuView>();

        // CE COMPTE NE PEUT PAS SE DÉDUIRE DES VUES CONSTRUITES.
        //
        // Pour le restaurateur, elles contiennent les plats épuisés et les cartes
        // hors créneau. Le comptage doit donc suivre la règle du CLIENT, quelle
        // que soit l'audience — sinon un restaurateur lirait « ouvert » là où son
        // client lit « tout est épuisé », et ne comprendrait pas pourquoi personne
        // ne commande.
        var resteQuelqueChose = false;

        foreach (var carte in cartes.OrderBy(m => m.DisplayOrder).ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            var carteEstServie = carte.IsServedAt(maintenant);

            // Hors créneau, la carte disparaît de la vitrine. Le restaurateur, lui,
            // continue de la voir — sinon il ne pourrait plus modifier son menu du
            // soir avant 18 h.
            if (!carteEstServie && !toutVoir)
            {
                continue;
            }

            var sectionsVues = new List<MenuSectionView>();

            foreach (var section in sections
                .Where(s => s.MenuId == carte.Id.Value)
                .OrderBy(s => s.DisplayOrder))
            {
                // Une section masquée disparaît de la vitrine, mais reste visible
                // du restaurateur — sinon il ne pourrait plus la rouvrir.
                if (!section.IsActive && !toutVoir)
                {
                    continue;
                }

                var lignes = articles
                    .Where(a => a.MenuCategoryId == section.Id.Value)
                    .Where(a => toutVoir || a.IsOrderableAt(maintenant))
                    .OrderBy(a => a.DisplayOrder)
                    .Select(a => ToItemView(a, maintenant, etablissementSert && carteEstServie))
                    .ToList();

                // LE COMPTE SUIT TOUJOURS LA RÈGLE DU CLIENT : carte servie,
                // section visible, article commandable. Les trois.
                if (carteEstServie && section.IsActive)
                {
                    resteQuelqueChose |= articles.Any(a =>
                        a.MenuCategoryId == section.Id.Value && a.IsOrderableAt(maintenant));
                }

                // Une section vide n'a rien à faire dans la vitrine — elle
                // donnerait l'impression d'un menu incomplet.
                if (lignes.Count == 0 && !toutVoir)
                {
                    continue;
                }

                sectionsVues.Add(new MenuSectionView(
                    section.Id.Value, section.Name, section.Description, section.IsActive, lignes));
            }

            if (sectionsVues.Count == 0 && !toutVoir)
            {
                continue;
            }

            vues.Add(new MenuView(
                carte.Id.Value,
                carte.Name,
                carte.Description,
                carte.IsActive,
                carteEstServie,
                carte.Window.StartTime?.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                carte.Window.EndTime?.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                carte.Window.AvailableFrom?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                carte.Window.AvailableUntil?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                sectionsVues));
        }

        var blocage = restaurant.CanAcceptOrders(maintenant, resteQuelqueChose);

        return Result.Success(new RestaurantMenuView(
            restaurant.Id.Value,
            restaurant.Name,
            blocage == OrderingBlockedReason.None,
            blocage.ToString(),
            restaurant.PreparationMinutes,
            vues));
    }

    private static MenuItemView ToItemView(MenuItem item, DateTime nowUtc, bool orderableContext)
        => new(
            item.Id.Value,
            item.Name,
            item.Description,
            item.ImageMediaId,
            item.LegacyImageUrl,

            // Le repli, une seule fois, ici. Voir `MenuItemView.DisplayImageUrl`.
            item.ImagePublicUrl ?? item.LegacyImageUrl,
            item.BasePrice.Amount,
            item.BasePrice.Currency,

            // TROIS CONDITIONS, PAS UNE. L'article doit être disponible, sa
            // carte servie à cette heure, et l'établissement en train de prendre
            // des commandes. Un plat du menu du midi affiché « commandable » à 20 h
            // ferait choisir le client puis refuserait son panier.
            orderableContext && item.IsOrderableAt(nowUtc),

            // `HasImage`, PAS `DisplayImageUrl is not null`. Une URL obsolète
            // (bucket renommé) ne doit pas faire croire à l'absence de photo, et
            // c'est le `mediaId` qui décide de la vendabilité. Les deux champs
            // peuvent donc légitimement se contredire : `HasImage` vrai, adresse
            // cassée — et c'est ce cas-là qu'il faut pouvoir diagnostiquer.
            item.HasImage,

            // Le RETOUR est annoncé quand il est connu : « de retour demain »
            // vaut mieux que « indisponible », qui ne dit pas s'il faut revenir.
            item.Availability.UnavailableUntilUtc,
            item.OptionGroups
                .OrderBy(g => g.DisplayOrder)
                .Select(g => new OptionGroupView(
                    g.Id,
                    g.Name,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsRequired,
                    g.Options
                        .Select(o => new OptionView(
                            o.Id, o.Name, o.PriceDelta, o.Availability.IsAvailableAt(nowUtc)))
                        .ToList()))
                .ToList());
}
