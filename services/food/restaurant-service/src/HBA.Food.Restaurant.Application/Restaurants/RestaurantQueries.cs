using HBA.Food.Contracts;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Restaurants;

/// <summary>La file des dossiers en attente de validation.</summary>
public sealed record ListPendingRestaurantsQuery(int Take = 100) : IQuery<IReadOnlyList<RestaurantSummary>>;

internal sealed class RestaurantQueryHandler
    : IQueryHandler<ListPendingRestaurantsQuery, IReadOnlyList<RestaurantSummary>>
{
    private const int MaxTake = 200;

    private readonly IRestaurantRepository _restaurants;

    public RestaurantQueryHandler(IRestaurantRepository restaurants) => _restaurants = restaurants;

    public async Task<Result<IReadOnlyList<RestaurantSummary>>> Handle(
        ListPendingRestaurantsQuery query, CancellationToken cancellationToken)
    {
        var dossiers = await _restaurants.ListByStatusAsync(
            RestaurantStatus.PendingApproval, Math.Clamp(query.Take, 1, MaxTake), cancellationToken);

        // L'heure est lue UNE fois pour toute la projection : la relire par
        // établissement ferait qu'un même écran répondrait à deux instants.
        var maintenant = DateTime.UtcNow;

        IReadOnlyList<RestaurantSummary> vues = dossiers.Select(r => Project(r, maintenant)).ToList();
        return Result.Success(vues);
    }

    private static RestaurantSummary Project(Restaurant r, DateTime nowUtc)
    {
        // SURCHARGE À UN SEUL PARAMÈTRE, DÉLIBÉRÉMENT.
        var blocage = r.CanAcceptOrders(nowUtc);

        return new RestaurantSummary(
            r.Id.Value,
            r.OwnerUserId,
            r.Name,
            r.Description,
            r.LogoMediaId,
            r.CoverMediaId,
            r.LegacyLogoUrl,
            r.Phone,
            r.Status.ToString(),
            blocage == OrderingBlockedReason.None,
            blocage.ToString(),
            r.PreparationMinutes,
            r.AcceptanceMode.ToString(),
            r.MinimumOrderAmount,

            // CHARGE NON CALCULÉE, ET C'EST CORRECT ICI.
            nameof(KitchenLoadLevel.Normal),
            0,

            // Un dossier en attente n'a pas de vitrine : le motif d'une fermeture
            // exceptionnelle n'y sert à rien.
            null,

            r.FulfillmentLocationId,
            r.PayoutSellerId,
            r.ServiceHours
                // Lundi en tête : DayOfWeek vaut Sunday = 0.
                .OrderBy(h => ((int)h.Day + 6) % 7)
                .ThenBy(h => h.OpensAt)
                .Select(h => new ServiceHoursSummary(
                    h.Day.ToString(),
                    h.OpensAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
                    h.ClosesAt.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture)))
                .ToList(),
            r.IsPubliclyVisible);
    }
}
