using HBA.Food.Contracts;
using HBA.Food.Domain;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Domain.Staff;
using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

using HBA.Food.Application.Orders;

namespace HBA.Food.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Food. Lecture seule.
///
/// AUCUN CACHE, CONTRAIREMENT À CELLE DES VENDEURS.
///
/// Cette lecture sert à AUTORISER une commande : servir une réponse périmée de
/// dix minutes accepterait des repas d'un restaurant qui vient de fermer, ou qui
/// s'est mis en pause parce que sa bouteille de gaz est vide. La fraîcheur prime
/// sur le nombre de requêtes.
/// </summary>
internal sealed class FoodModuleApi : IFoodModuleApi
{
    private readonly FoodDbContext _dbContext;

    public FoodModuleApi(FoodDbContext dbContext) => _dbContext = dbContext;

    public async Task<RestaurantSummary?> GetRestaurantAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var id = new RestaurantId(restaurantId);
        var restaurant = await _dbContext.Restaurants
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        return restaurant is null ? null : await MapAsync(restaurant, cancellationToken);
    }

    public async Task<RestaurantSummary?> GetRestaurantByOwnerAsync(
        Guid ownerUserId, CancellationToken cancellationToken = default)
    {
        var restaurant = await _dbContext.Restaurants
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OwnerUserId == ownerUserId, cancellationToken);

        return restaurant is null ? null : await MapAsync(restaurant, cancellationToken);
    }

    private async Task<RestaurantSummary> MapAsync(Restaurant restaurant, CancellationToken cancellationToken)
    {
        // L'HEURE EST LUE ICI, ET NULLE PART DANS LE DOMAINE. C'est la
        // frontière : au-delà, tout se teste en passant un instant.
        var maintenant = DateTime.UtcNow;

        // LA CHARGE EST LUE MÊME QUAND L'ÉTABLISSEMENT EST BLOQUÉ.
        //
        // « Forte demande » et « fermé » ne sont pas le même axe : un restaurant
        // saturé n'est pas fermé, il est LENT. L'écran doit pouvoir afficher les
        // deux, et un client qui voit « saturé » sait qu'il peut revenir dans dix
        // minutes — là où « fermé » l'enverrait ailleurs pour la soirée.
        var charge = restaurant.AssessLoad(
            await _dbContext.FoodOrders
                .AsNoTracking()
                .CountAsync(
                    o => o.RestaurantId == restaurant.Id.Value
                        && (o.Status == FoodOrderStatus.PendingRestaurantAcceptance
                            || o.Status == FoodOrderStatus.Accepted
                            || o.Status == FoodOrderStatus.Preparing
                            || o.Status == FoodOrderStatus.ReadyForPickup),
                    cancellationToken));

        var etatDuLieu = restaurant.CanAcceptOrders(maintenant);

        // LA CARTE N'EST INTERROGÉE QUE SI RIEN D'AUTRE NE BLOQUE DÉJÀ.
        //
        // Double raison. La première est un motif : un restaurant fermé dont la
        // carte est vide doit répondre « fermé », pas « épuisé » — « revenez
        // demain » aide, « tout est épuisé » laisse croire qu'il suffit d'attendre
        // le prochain plat. La seconde est le coût : cette lecture autorise chaque
        // commande, et les cas bloqués — la nuit, une pause, une suspension —
        // n'ont plus rien à demander.
        var blocage = etatDuLieu != OrderingBlockedReason.None
            ? etatDuLieu
            : restaurant.CanAcceptOrders(
                maintenant,
                await HasOrderableItemAsync(restaurant.Id.Value, maintenant, cancellationToken));

        return new RestaurantSummary(
            restaurant.Id.Value,
            restaurant.OwnerUserId,
            restaurant.Name,
            restaurant.Description,
            restaurant.LogoMediaId,
            restaurant.CoverMediaId,
            restaurant.LegacyLogoUrl,
            restaurant.Phone,
            restaurant.Status.ToString(),
            blocage == OrderingBlockedReason.None,
            blocage.ToString(),
            restaurant.PreparationMinutes,
            restaurant.AcceptanceMode.ToString(),
            restaurant.MinimumOrderAmount,
            charge.Level.ToString(),
            charge.ExtraWaitMinutes,

            // Le motif de l'exception du JOUR, s'il y en a une qui ferme.
            restaurant.SpecialHours
                .FirstOrDefault(e => e.Date == BeninTime.LocalDate(maintenant) && e.IsClosed)?.Reason,

            restaurant.FulfillmentLocationId,
            restaurant.PayoutSellerId,
            restaurant.ServiceHours
                // Lundi en tête : DayOfWeek vaut Sunday = 0, et une semaine
                // commence le lundi au Bénin comme en France.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new ServiceHoursSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            restaurant.IsPubliclyVisible);
    }

    /// <summary>
    /// L'appartenance d'un compte au personnel d'un établissement.
    ///
    /// SANS SUIVI EF, ET AVEC SES DÉROGATIONS.
    ///
    /// Le `Include` n'est pas optionnel : les dérogations sont une collection
    /// possédée dans une table à part. Sans elles, `EffectivePermissions` ne
    /// rendrait que les défauts du rôle — un caissier à qui l'on a nommément
    /// accordé la gestion de la carte se verrait refuser, et un manager à qui on
    /// l'a retirée passerait quand même. Les deux erreurs sont silencieuses.
    /// </summary>
    public async Task<FoodStaffMembership?> GetStaffMembershipAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var membre = await _dbContext.Staff
            .AsNoTracking()
            .Include("_overrides")
            .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive, cancellationToken);

        if (membre is null)
        {
            return null;
        }

        return new FoodStaffMembership(
            membre.RestaurantId,
            membre.Id.Value,
            membre.UserId,
            membre.Role.ToString(),
            membre.IsActive,
            membre.IsFounder,
            membre.EffectivePermissions.Select(p => p.ToCode()).ToList());
    }

    /// <summary>
    /// Les rattachements d'un ticket. Sans suivi EF : lecture pure, appelée par le
    /// retour de course pour retrouver la commande à clore.
    /// </summary>
    public async Task<FoodOrderRef?> GetOrderAsync(
        Guid foodOrderId, CancellationToken cancellationToken = default)
    {
        var id = new FoodOrderId(foodOrderId);

        var commande = await _dbContext.FoodOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return commande is null
            ? null
            : new FoodOrderRef(
                commande.Id.Value, commande.OrderId, commande.RestaurantId, commande.Status.ToString(),
                FoodOrderOriginTranslation.Traduire(commande.Origin));
    }

    /// <summary>
    /// Un article de carte, avec ses groupes d'options et leurs écarts de prix.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST LA LECTURE QUI PERMET AU PANIER DE NE PLUS CROIRE LE CLIENT.
    ///
    /// Voir le contrat. Trois conséquences ici :
    ///
    /// LE RESTAURANT EST VÉRIFIÉ, PAS SEULEMENT L'ARTICLE. Un identifiant de plat
    /// appartenant à un AUTRE établissement rend `null`. Sans ce filtre, un client
    /// composerait un panier « chez A » avec le plat le moins cher de B, et
    /// l'invariant mono-restaurant du panier n'y verrait rien : il compare des
    /// `RestaurantId` qu'on lui a donnés.
    ///
    /// `IsOrderable` COMBINE QUATRE CONDITIONS, comme la carte publique : le lieu
    /// prend des commandes, la carte est servie à cette heure, la section est
    /// visible, l'article est disponible. En omettre une ferait accepter dans le
    /// panier ce que la carte refuse d'afficher — et le client découvrirait le
    /// refus au paiement.
    ///
    /// AUCUN CACHE, pour la raison écrite en tête de cette classe : ce prix
    /// autorise un encaissement.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<MenuItemView?> GetMenuItemAsync(
        Guid restaurantId, Guid menuItemId, CancellationToken cancellationToken = default)
    {
        var id = new MenuItemId(menuItemId);

        // Les groupes d'options sont « owned » : EF les charge avec la racine.
        var article = await _dbContext.MenuItems
            .AsNoTracking()
            .FirstOrDefaultAsync(
                i => i.Id == id && i.RestaurantId == restaurantId, cancellationToken);

        if (article is null)
        {
            return null;
        }

        var maintenant = DateTime.UtcNow;

        var restaurant = await _dbContext.Restaurants
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == new RestaurantId(restaurantId), cancellationToken);

        // UN ÉTABLISSEMENT INVISIBLE N'A PAS DE CARTE LISIBLE DE L'EXTÉRIEUR.
        //
        // Même règle que `GetMenuQuery` pour le public : un dossier en brouillon
        // ou suspendu répond « introuvable », plutôt que « voici son plat, mais
        // vous ne pouvez pas le commander ».
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return null;
        }

        // COMPARAISON PAR L'IDENTITÉ FORTE, PAS PAR `.Value`.
        //
        // `c.Id.Value == …` oblige EF à traduire un accès de propriété sur un
        // type converti ; selon la version du fournisseur, il rend soit une
        // requête qui ne compile pas, soit un filtrage remonté en mémoire — donc
        // le chargement de toutes les sections du dépôt pour en garder une.
        var sectionId = new MenuCategoryId(article.MenuCategoryId);
        var section = await _dbContext.MenuCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == sectionId, cancellationToken);

        Menu? carte = null;
        if (section is not null)
        {
            var carteId = new MenuId(section.MenuId);
            carte = await _dbContext.Menus
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == carteId, cancellationToken);
        }

        var contexteCommandable =
            restaurant.CanAcceptOrders(maintenant) == OrderingBlockedReason.None
            && section is not null && section.IsActive
            && carte is not null && carte.IsActive && carte.IsServedAt(maintenant);

        return new MenuItemView(
            article.Id.Value,
            article.Name,
            article.Description,
            article.ImageMediaId,
            article.LegacyImageUrl,
            article.ImagePublicUrl ?? article.LegacyImageUrl,
            article.BasePrice.Amount,
            article.BasePrice.Currency,
            contexteCommandable && article.IsOrderableAt(maintenant),
            article.HasImage,
            article.Availability.UnavailableUntilUtc,
            article.OptionGroups
                .OrderBy(g => g.DisplayOrder)
                .Select(g => new OptionGroupView(
                    g.Id,
                    g.Name,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsRequired,
                    g.Options
                        .Select(o => new OptionView(
                            o.Id, o.Name, o.PriceDelta, o.Availability.IsAvailableAt(maintenant)))
                        .ToList()))
                .ToList());
    }

    /// <summary>
    /// Reste-t-il au moins UN article commandable ?
    ///
    /// LE FILTRE HORAIRE SE FAIT EN MÉMOIRE, ET C'EST ASSUMÉ.
    ///
    /// `MenuItemConfiguration` l'explique déjà pour l'index : la disponibilité
    /// dépend de l'heure, PostgreSQL refuse un index partiel dont le prédicat
    /// n'est pas immuable, et « maintenant » ne l'est pas. On charge donc les
    /// quelques dizaines de lignes d'une carte et l'on tranche ici.
    ///
    /// Ce compromis vaut À L'ÉCHELLE D'UN RESTAURANT. Il cesserait de valoir
    /// pour « tous les plats disponibles de la ville ».
    ///
    /// LES SECTIONS MASQUÉES SONT ÉCARTÉES D'ABORD : un article rangé dans une
    /// section que le restaurateur a masquée n'apparaît sur aucun écran, il ne
    /// peut donc rendre l'établissement commandable.
    ///
    /// LES GROUPES D'OPTIONS COMPTENT. `IsOrderableAt` refuse un plat dont une
    /// taille obligatoire est épuisée — pas seulement un plat marqué indisponible.
    /// Un pré-filtre SQL sur la seule disponibilité de l'article se tromperait
    /// dans la mauvaise direction : il annoncerait commandable ce que le panier
    /// refuserait ensuite.
    /// </summary>
    private async Task<bool> HasOrderableItemAsync(
        Guid restaurantId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // TROIS NIVEAUX À TRAVERSER DEPUIS LA BASCULE À DEUX NIVEAUX.
        //
        // Avant, il suffisait d'écarter les sections masquées. Il faut désormais
        // écarter d'abord les CARTES hors créneau — à 20 h, le menu du midi et
        // tous ses plats ne comptent pour rien, même s'ils sont disponibles et
        // dans une section visible.
        //
        // C'est précisément le cas que le second niveau existe pour représenter,
        // et l'oublier ici annoncerait « ouvert » un restaurant dont la seule
        // carte servie est vide.
        var cartes = await _dbContext.Menus
            .AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId && m.IsActive)
            .ToListAsync(cancellationToken);

        // Le filtre horaire est en mémoire — le prédicat dépend de l'heure, et
        // PostgreSQL refuse un index partiel dont la condition n'est pas immuable.
        var carteServies = cartes
            .Where(m => m.IsServedAt(nowUtc))
            .Select(m => m.Id.Value)
            .ToHashSet();

        if (carteServies.Count == 0)
        {
            return false;
        }

        var sections = await _dbContext.MenuCategories
            .AsNoTracking()
            .Where(c => c.RestaurantId == restaurantId && c.IsActive)
            .ToListAsync(cancellationToken);

        var sectionsRetenues = sections
            .Where(c => carteServies.Contains(c.MenuId))
            .Select(c => c.Id.Value)
            .ToList();

        if (sectionsRetenues.Count == 0)
        {
            return false;
        }

        var articles = await _dbContext.MenuItems
            .AsNoTracking()
            .Where(i => i.RestaurantId == restaurantId && sectionsRetenues.Contains(i.MenuCategoryId))
            .ToListAsync(cancellationToken);

        return articles.Any(i => i.IsOrderableAt(nowUtc));
    }
}
