using HBA.Food.Contracts;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>L'établissement du compte connecté, avec son rôle.</summary>
public sealed record GetMyRestaurantQuery(Guid UserId) : IQuery<PartnerRestaurantView>;

internal sealed class PartnerRestaurantQueryHandler
    : IQueryHandler<GetMyRestaurantQuery, PartnerRestaurantView>
{
    private readonly IFoodModuleApi _food;

    public PartnerRestaurantQueryHandler(IFoodModuleApi food) => _food = food;

    public async Task<Result<PartnerRestaurantView>> Handle(
        GetMyRestaurantQuery query, CancellationToken cancellationToken)
    {
        var membre = await _food.GetStaffMembershipAsync(query.UserId, cancellationToken);

        if (membre is null)
        {
            // Compte valide, mais qui ne travaille dans aucun établissement.
            return Result.Failure<PartnerRestaurantView>(
                Error.NotFound("Food.Membership.NotFound", "Aucun établissement pour ce compte."));
        }

        // LECTURE NON FILTRÉE SUR LA VISIBILITÉ, DÉLIBÉRÉMENT.
        var restaurant = await _food.GetRestaurantAsync(membre.RestaurantId, cancellationToken);

        if (restaurant is null)
        {
            // Appartenance orpheline : l'établissement a été supprimé sans que la
            // ligne de personnel le soit.
            return Result.Failure<PartnerRestaurantView>(
                Error.NotFound("Food.Restaurant.NotFound", "Établissement introuvable."));
        }

        return Result.Success(new PartnerRestaurantView(
            restaurant.Id,
            restaurant.Name,
            restaurant.Status,
            membre.Role,
            membre.IsFounder,
            membre.IsActive,
            membre.Permissions,
            restaurant.PayoutSellerId,
            restaurant.AcceptsOrdersNow,
            restaurant.BlockedReason));
    }
}
