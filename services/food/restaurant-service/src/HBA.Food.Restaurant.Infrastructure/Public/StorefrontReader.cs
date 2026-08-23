using HBA.Food.Application.Abstractions;
using HBA.Food.Contracts;
using HBA.Food.Domain;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Food.Infrastructure.Public;

/// <summary>
/// Lecture de la vitrine. Lecture seule, sans cache.
/// </summary>
/// <remarks>
/// DÉPEND DE <see cref="IFoodModuleApi"/> POUR LA FICHE, ET C'EST LÉGITIME ICI.
///
/// La projection complète d'un établissement — charge de cuisine, blocage,
/// fermeture exceptionnelle du jour, carte réellement servie à cette heure — est
/// subtile et existe DÉJÀ, une fois, dans <c>FoodModuleApi</c>. La réécrire
/// donnerait une seconde version qui divergerait au premier changement de règle.
///
/// Cette dépendance est acceptable parce qu'on est dans Infrastructure : la
/// couche Application, elle, ne connaît que <see cref="IStorefrontReader"/>.
/// </remarks>
internal sealed class StorefrontReader : IStorefrontReader
{
    /// <summary>
    /// Plafond d'une page de vitrine.
    /// </summary>
    /// <remarks>
    /// BORNE CÔTÉ SERVICE, EN PLUS DE CELLE DE LA PASSERELLE.
    ///
    /// La passerelle borne déjà `pageSize`. S'en remettre à elle supposerait
    /// qu'elle soit le SEUL appelant — une limite qui n'existe qu'au bord se
    /// contourne en entrant par une autre porte.
    /// </remarks>
    private const int MaxPageSize = 50;

    private readonly FoodDbContext _dbContext;
    private readonly IFoodModuleApi _food;

    public StorefrontReader(FoodDbContext dbContext, IFoodModuleApi food)
    {
        _dbContext = dbContext;
        _food = food;
    }

    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX REQUÊTES POUR TOUTE LA PAGE, ET NON DEUX PAR ÉTABLISSEMENT.
    ///
    /// Réutiliser la projection complète ici aurait été le geste évident. Il
    /// aurait coûté, PAR établissement, un comptage des commandes actives et
    /// jusqu'à quatre lectures de carte — quatre-vingts requêtes pour une page de
    /// vingt, sur l'écran d'entrée de l'application. C'est le N+1 dont personne ne
    /// s'aperçoit avant la mise en charge.
    ///
    /// D'où : une requête pour les établissements, une requête GROUPÉE pour les
    /// commandes actives de la page, et aucune lecture de carte.
    ///
    /// CONSÉQUENCE ASSUMÉE : `IsOpenNow` NE VÉRIFIE PAS LA CARTE.
    ///
    /// Un établissement ouvert dont tout est épuisé apparaîtra « ouvert » dans la
    /// liste. La fiche, elle, rend la réponse ferme. C'est pourquoi le champ
    /// s'appelle `IsOpenNow` et non `AcceptsOrdersNow` — le nom porte la promesse
    /// exacte, ni plus, ni moins.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<IReadOnlyList<RestaurantCardView>> ListAsync(
        int skip, int take, CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(take, 1, MaxPageSize);
        var offset = Math.Max(skip, 0);

        var restaurants = await _dbContext.Restaurants
            .AsNoTracking()
            .Where(r => r.Status == RestaurantStatus.Active)
            // Ordre STABLE, sans quoi la page 2 redonnerait des établissements de
            // la page 1 et en omettrait d'autres définitivement.
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip(offset)
            .Take(page)
            .ToListAsync(cancellationToken);

        if (restaurants.Count == 0)
        {
            return [];
        }

        var ids = restaurants.Select(r => r.Id.Value).ToList();

        var chargeParRestaurant = await _dbContext.FoodOrders
            .AsNoTracking()
            .Where(o => ids.Contains(o.RestaurantId)
                && (o.Status == FoodOrderStatus.PendingRestaurantAcceptance
                    || o.Status == FoodOrderStatus.Accepted
                    || o.Status == FoodOrderStatus.Preparing
                    || o.Status == FoodOrderStatus.ReadyForPickup))
            .GroupBy(o => o.RestaurantId)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, cancellationToken);

        // L'heure est lue UNE fois pour toute la page : la relire par
        // établissement ferait qu'un même écran répondrait à deux instants.
        var maintenant = DateTime.UtcNow;
        var aujourdhui = BeninTime.LocalDate(maintenant);

        return restaurants.Select(restaurant =>
        {
            var charge = restaurant.AssessLoad(
                chargeParRestaurant.GetValueOrDefault(restaurant.Id.Value, 0));

            // Surcharge à UN paramètre : lieu, horaires, pause, fermeture
            // exceptionnelle. La carte n'est pas interrogée — cf. remarques.
            var blocage = restaurant.CanAcceptOrders(maintenant);

            return new RestaurantCardView(
                restaurant.Id.Value,
                restaurant.Name,
                restaurant.Description,
                restaurant.LogoMediaId,
                restaurant.LegacyLogoUrl,
                blocage == OrderingBlockedReason.None,
                blocage.ToString(),
                restaurant.PreparationMinutes,
                restaurant.MinimumOrderAmount,
                charge.Level.ToString(),
                charge.ExtraWaitMinutes,
                restaurant.SpecialHours
                    .FirstOrDefault(e => e.Date == aujourdhui && e.IsClosed)?.Reason);
        }).ToList();
    }

    public async Task<RestaurantSummary?> GetPublicAsync(
        Guid restaurantId, CancellationToken cancellationToken = default)
    {
        var restaurant = await _food.GetRestaurantAsync(restaurantId, cancellationToken);

        // LE FILTRE EST ICI, ET IL EST TOUT L'ÉCART AVEC LA LECTURE INTERNE.
        //
        // `GetRestaurantAsync` rend N'IMPORTE QUEL établissement, y compris un
        // dossier en brouillon ou suspendu : c'est ce qu'il faut à l'espace du
        // restaurateur et à la file de validation. L'exposer tel quel sur une
        // route anonyme laisserait consulter un établissement écarté de la
        // plateforme.
        return restaurant?.IsPubliclyVisible == true ? restaurant : null;
    }
}
