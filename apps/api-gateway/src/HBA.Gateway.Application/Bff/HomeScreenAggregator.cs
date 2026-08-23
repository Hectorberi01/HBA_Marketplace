using HBA.Gateway.Application.Abstractions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Application.Bff;

/// <summary>
/// Compose un écran agrégé en interrogeant plusieurs services en parallèle.
/// </summary>
public sealed class HomeScreenAggregator
{
    private readonly IServiceClientRegistry _clients;
    private readonly ICorrelationContext _correlation;
    private readonly BffAggregationOptions _options;
    private readonly ILogger<HomeScreenAggregator> _logger;

    public HomeScreenAggregator(
        IServiceClientRegistry clients,
        ICorrelationContext correlation,
        IOptions<BffAggregationOptions> options,
        ILogger<HomeScreenAggregator> logger)
    {
        _clients = clients;
        _correlation = correlation;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Construit l'écran <paramref name="screenId"/> pour la surface indiquée.
    /// </summary>
    /// <param name="screenId">Identifiant de l'écran dans <c>Bff:Screens</c>.</param>
    /// <param name="surface">« express » ou « food », reporté tel quel dans la réponse.</param>
    public async Task<BffHomeResponse> BuildAsync(
        string screenId, string surface, CancellationToken cancellationToken)
    {
        if (!_options.Screens.TryGetValue(screenId, out var definitions) || definitions.Count == 0)
        {
            // UN ÉCRAN NON CONFIGURÉ REND UNE RÉPONSE VIDE, PAS UNE ERREUR 500.
            //
            // Au démarrage de la plateforme, aucun service n'est déployé : c'est
            // l'état NORMAL, pas une panne. Une 500 ici mettrait la passerelle en
            // échec dans les tableaux de bord et masquerait les vraies pannes.
            _logger.LogInformation(
                "Écran BFF {ScreenId} non configuré : réponse vide. [CorrelationId={CorrelationId}]",
                screenId, _correlation.CorrelationId);

            return new BffHomeResponse(surface, _correlation.CorrelationId, []);
        }

        // ═════════════════════════════════════════════════════════════════════
        // CE `CancellationTokenSource` DOIT ÊTRE LIÉ À CELUI DE LA REQUÊTE.
        //
        // Sans `CreateLinkedTokenSource`, un client qui raccroche laisserait la
        // passerelle terminer ses treize appels sortants pour personne. Sous
        // charge, ces requêtes fantômes saturent les pools de connexions et les
        // disjoncteurs s'ouvrent sur des services qui allaient parfaitement bien.
        // ═════════════════════════════════════════════════════════════════════
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        var sections = await Task.WhenAll(
            definitions.Select(definition => FetchAsync(definition, budget.Token)));

        return new BffHomeResponse(surface, _correlation.CorrelationId, sections);
    }

    private async Task<BffSection> FetchAsync(
        BffSectionDefinition definition, CancellationToken cancellationToken)
    {
        var client = _clients.Find(definition.Service);

        if (client is null)
        {
            // La validation au démarrage refuse déjà ce cas ; ce garde protège la
            // configuration rechargée à chaud, qui n'est pas revalidée.
            _logger.LogError(
                "Section BFF {Key} : service {Service} inconnu. [CorrelationId={CorrelationId}]",
                definition.Key, definition.Service, _correlation.CorrelationId);

            return BffSection.Unavailable(definition.Key);
        }

        try
        {
            var result = await client.GetJsonAsync(definition.Path, cancellationToken);

            if (result is { IsSuccess: true, Payload: not null })
            {
                return BffSection.Ok(definition.Key, result.Payload.Value);
            }

            _logger.LogWarning(
                "Section BFF {Key} indisponible ({Service} → {Status}) : {Reason}. [CorrelationId={CorrelationId}]",
                definition.Key, definition.Service, result.StatusCode, result.FailureReason,
                _correlation.CorrelationId);

            return BffSection.Unavailable(definition.Key);
        }
        catch (OperationCanceledException)
        {
            // ON NE RELANCE PAS, MÊME SI LE CLIENT A RACCROCHÉ.
            //
            // `Task.WhenAll` propage la PREMIÈRE exception : une seule section
            // annulée ferait échouer l'écran entier, y compris les sections déjà
            // revenues avec succès. Le dépassement de budget doit dégrader, pas
            // annuler. Si c'est le client qui est parti, la réponse construite ici
            // ne sera de toute façon écrite nulle part.
            _logger.LogInformation(
                "Section BFF {Key} abandonnée (budget dépassé ou client parti). [CorrelationId={CorrelationId}]",
                definition.Key, _correlation.CorrelationId);

            return BffSection.Unavailable(definition.Key);
        }
    }
}
