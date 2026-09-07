using HBA.Food.Application.Abstractions;
using HBA.Food.Contracts;
using HBA.Food.Domain;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Food.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Food.Infrastructure.Public;

/// <summary>Lecture de la vitrine. Lecture seule, sans cache.</summary>
internal sealed class StorefrontReader : IStorefrontReader
{
    /// <summary>Plafond d'une page de vitrine.</summary>
    private const int MaxPageSize = 50;

    private readonly FoodDbContext _dbContext;
    private readonly IFoodModuleApi _food;

    public StorefrontReader(FoodDbContext dbContext, IFoodModuleApi food)
    {
        _dbContext = dbContext;
        _food = food;
    }

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

        // L'heure est lue UNE fois pour toute la page : la relire par établissement
        // ferait qu'un même écran répondrait à deux instants.
        var maintenant = DateTime.UtcNow;
        var aujourdhui = BeninTime.LocalDate(maintenant);

        return restaurants.Select(restaurant =>
        {
            var charge = restaurant.AssessLoad(
                chargeParRestaurant.GetValueOrDefault(restaurant.Id.Value, 0));

            // Surcharge à UN paramètre : lieu, horaires, pause, fermeture
            // exceptionnelle.
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
        return restaurant?.IsPubliclyVisible == true ? restaurant : null;
    }
}
