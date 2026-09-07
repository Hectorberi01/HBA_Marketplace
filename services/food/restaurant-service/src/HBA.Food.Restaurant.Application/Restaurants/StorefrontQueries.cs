using HBA.Food.Application.Abstractions;
using HBA.Food.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>
/// La vitrine publique : ce qu'un client voit avant d'avoir choisi un restaurant.
/// </summary>
public sealed record ListStorefrontQuery(int Page = 1, int PageSize = 20)
    : IQuery<IReadOnlyList<RestaurantCardView>>;

/// <summary>La fiche d'un établissement, telle qu'un client la voit.</summary>
public sealed record GetPublicRestaurantQuery(Guid RestaurantId) : IQuery<RestaurantSummary>;

internal sealed class StorefrontQueryHandler
    : IQueryHandler<ListStorefrontQuery, IReadOnlyList<RestaurantCardView>>,
      IQueryHandler<GetPublicRestaurantQuery, RestaurantSummary>
{
    private const int MaxPageSize = 50;
    private const int DefaultPageSize = 20;

    private readonly IStorefrontReader _storefront;

    public StorefrontQueryHandler(IStorefrontReader storefront) => _storefront = storefront;

    public async Task<Result<IReadOnlyList<RestaurantCardView>>> Handle(
        ListStorefrontQuery query, CancellationToken cancellationToken)
    {
        // Bornes appliquées ici ET dans le module : une limite qui n'existe qu'à un
        // seul niveau se contourne en entrant par une autre porte.
        var pageSize = query.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : query.PageSize;
        var page = query.Page < 1 ? 1 : query.Page;

        var cartes = await _storefront.ListAsync(
            (page - 1) * pageSize, pageSize, cancellationToken);

        return Result.Success(cartes);
    }

    public async Task<Result<RestaurantSummary>> Handle(
        GetPublicRestaurantQuery query, CancellationToken cancellationToken)
    {
        // Le lecteur rend déjà `null` dans les DEUX cas — inexistant et hors
        // vitrine.
        var restaurant = await _storefront.GetPublicAsync(query.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure<RestaurantSummary>(
                Error.NotFound("Food.Restaurant.NotFound", "Établissement introuvable."));
        }

        return Result.Success(restaurant);
    }
}
