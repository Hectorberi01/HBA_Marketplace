using HBA.Gateway.Application.DTOs;

namespace HBA.Gateway.Application.Bff.Client.Food;

/// <summary>Écran d'accueil HBA Food (restauration).</summary>
public sealed class FoodHomeService
{
    public const string ScreenId = "client.food.home";
    public const string Surface = "food";

    private readonly HomeScreenAggregator _aggregator;

    public FoodHomeService(HomeScreenAggregator aggregator) => _aggregator = aggregator;

    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _aggregator.BuildAsync(ScreenId, Surface, cancellationToken);
}
