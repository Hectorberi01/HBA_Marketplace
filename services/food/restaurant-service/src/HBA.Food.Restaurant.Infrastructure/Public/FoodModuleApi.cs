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

/// <summary>Implémentation in-process de l'API publique du module Food.</summary>
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
        // L'HEURE EST LUE ICI, ET NULLE PART DANS LE DOMAINE. C'est la frontière :
        // au-delà, tout se teste en passant un instant.
        var maintenant = DateTime.UtcNow;

        // LA CHARGE EST LUE MÊME QUAND L'ÉTABLISSEMENT EST BLOQUÉ.
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

    /// <summary>L'appartenance d'un compte au personnel d'un établissement.</summary>
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

    /// <summary>Les rattachements d'un ticket.</summary>
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

    /// <summary>Un article de carte, avec ses groupes d'options et leurs écarts de prix.</summary>
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
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return null;
        }

        // COMPARAISON PAR L'IDENTITÉ FORTE, PAS PAR `.Value`.
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

    /// <summary>Reste-t-il au moins UN article commandable ?</summary>
    private async Task<bool> HasOrderableItemAsync(
        Guid restaurantId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        // TROIS NIVEAUX À TRAVERSER DEPUIS LA BASCULE À DEUX NIVEAUX.
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
