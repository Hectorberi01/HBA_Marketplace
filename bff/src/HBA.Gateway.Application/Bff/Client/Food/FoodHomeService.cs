using HBA.Gateway.Application.DTOs;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>Écran d'accueil HBA Food (restauration).</summary>
/// <remarks>
/// Voir <see cref="Express.ExpressHomeService"/> : la séparation des deux
/// façades est délibérée. Aucune section de cet écran ne doit interroger le
/// catalogue marketplace, et aucune section de l'écran Express ne doit
/// interroger food-service.
/// </remarks>
public sealed class FoodHomeService
{
    public const string ScreenId = "client.food.home";
    public const string Surface = "food";

    private readonly HomeScreenAggregator _aggregator;

    public FoodHomeService(HomeScreenAggregator aggregator) => _aggregator = aggregator;

    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _aggregator.BuildAsync(ScreenId, Surface, cancellationToken);
}
