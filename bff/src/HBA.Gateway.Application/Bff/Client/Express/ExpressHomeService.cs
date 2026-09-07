using HBA.Gateway.Application.DTOs;

namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Écran d'accueil HBAExpress (marketplace).</summary>
public sealed class ExpressHomeService
{
    public const string ScreenId = "client.express.home";
    public const string Surface = "express";

    private readonly HomeScreenAggregator _aggregator;

    public ExpressHomeService(HomeScreenAggregator aggregator) => _aggregator = aggregator;

    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _aggregator.BuildAsync(ScreenId, Surface, cancellationToken);
}
