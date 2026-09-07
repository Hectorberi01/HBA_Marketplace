using HBA.Inventory.Application.Stock.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Inventory.Infrastructure.BackgroundJobs;

/// <summary>Réglage du balayage d'expiration.</summary>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="BatchSize">Articles repris par tour.</param>
public sealed record StockReservationSweepOptions(TimeSpan Interval, int BatchSize);

/// <summary>Réglage de la PURGE des réservations terminées.</summary>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="Retention">Âge minimum d'une ligne terminée pour être effacée.</param>
/// <param name="BatchSize">Lignes effacées par tour.</param>
public sealed record StockReservationPurgeOptions(
    TimeSpan Interval, TimeSpan Retention, int BatchSize);

/// <summary>Rend à la vente les réservations dont l'échéance est dépassée.</summary>
internal sealed class ExpireStockReservationsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockReservationSweepOptions _options;
    private readonly ILogger<ExpireStockReservationsWorker> _logger;

    public ExpireStockReservationsWorker(
        IServiceScopeFactory scopeFactory,
        StockReservationSweepOptions options,
        ILogger<ExpireStockReservationsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // UNE PORTÉE PAR TOUR. Le `DbContext` est `Scoped` : le réutiliser
                // d'un tour sur l'autre garderait en suivi tous les articles déjà
                // balayés, et un incident laisserait ses entités modifiées dans le
                // contexte du tour suivant, qui les committerait.
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new ExpireStockReservationsCommand(_options.BatchSize), stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Expiration des reservations de stock : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (!resultat.Value.IsEmpty)
                {
                    // Le VOLUME d'abord : c'est lui qui dit combien de marchandise
                    // dormait.
                    _logger.LogInformation(
                        "Expiration des reservations de stock : {Quantity} unite(s) rendue(s) a la vente, "
                        + "{Reservations} reservation(s) expiree(s) sur {Items} article(s).",
                        resultat.Value.Quantity, resultat.Value.Reservations, resultat.Value.Items);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Un incident de base ne doit jamais tuer le travailleur : le tour
                // suivant reprendra les mêmes réservations, rien n'est perdu —
                // c'est précisément ce que l'idempotence du balayage garantit.
                _logger.LogError(ex, "Expiration des reservations de stock : echec du cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>Efface les réservations terminées trop anciennes pour servir encore.</summary>
internal sealed class PurgeStockReservationsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StockReservationPurgeOptions _options;
    private readonly ILogger<PurgeStockReservationsWorker> _logger;

    public PurgeStockReservationsWorker(
        IServiceScopeFactory scopeFactory,
        StockReservationPurgeOptions options,
        ILogger<PurgeStockReservationsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new PurgeStockReservationsCommand(_options.Retention, _options.BatchSize),
                    stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Purge des reservations de stock : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (resultat.Value > 0)
                {
                    // SILENCIEUX QUAND IL N'Y A RIEN À FAIRE, comme son voisin : au
                    // régime normal ce tour ne trouve rien pendant des mois, et une
                    // ligne quotidienne « 0 effacée » ferait perdre celles qui
                    // comptent.
                    _logger.LogInformation(
                        "Purge des reservations de stock : {Count} ligne(s) terminee(s) effacee(s) "
                        + "(retention {Days} jour(s)).",
                        resultat.Value, (int)_options.Retention.TotalDays);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Purge des reservations de stock : echec du cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
