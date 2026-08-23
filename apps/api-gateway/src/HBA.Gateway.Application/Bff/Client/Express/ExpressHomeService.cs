using HBA.Gateway.Application.DTOs;

namespace HBA.Gateway.Application.Bff.Client.Express;

/// <summary>Écran d'accueil HBAExpress (marketplace).</summary>
/// <remarks>
/// FAÇADE DISTINCTE DE CELLE DE HBA FOOD, VOLONTAIREMENT.
///
/// Les deux pourraient appeler <see cref="HomeScreenAggregator"/> directement
/// depuis un contrôleur commun paramétré. Ce raccourci ferait disparaître la
/// frontière entre les deux univers du CODE alors qu'elle doit exister pour le
/// client : c'est le point §13 du cahier des charges, et c'est aussi ce qui
/// permettra de séparer un jour les deux BFF sans démêler un contrôleur commun.
/// </remarks>
public sealed class ExpressHomeService
{
    public const string ScreenId = "client.express.home";
    public const string Surface = "express";

    private readonly HomeScreenAggregator _aggregator;

    public ExpressHomeService(HomeScreenAggregator aggregator) => _aggregator = aggregator;

    public Task<BffHomeResponse> GetHomeAsync(CancellationToken cancellationToken)
        => _aggregator.BuildAsync(ScreenId, Surface, cancellationToken);
}
