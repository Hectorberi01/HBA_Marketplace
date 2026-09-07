using HBA.Promotions.Application.Promotions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Promotions.Infrastructure.BackgroundJobs;

/// <summary>Réglage du balayage des retenues expirées.</summary>
/// <param name="Interval">Délai entre deux tours.</param>
/// <param name="BatchSize">Coupons repris par tour.</param>
public sealed record CouponHoldSweepOptions(TimeSpan Interval, int BatchSize);

/// <summary>
/// Rend au budget des campagnes les retenues de coupon dont l'échéance est passée.
/// </summary>
internal sealed class ExpireCouponHoldsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CouponHoldSweepOptions _options;
    private readonly ILogger<ExpireCouponHoldsWorker> _logger;

    public ExpireCouponHoldsWorker(
        IServiceScopeFactory scopeFactory,
        CouponHoldSweepOptions options,
        ILogger<ExpireCouponHoldsWorker> logger)
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
                // d'un tour sur l'autre garderait en suivi tous les coupons déjà
                // balayés, et un incident laisserait ses entités modifiées dans le
                // contexte du tour suivant, qui les committerait.
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new ExpireCouponHoldsCommand(_options.BatchSize), stoppingToken);

                if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Expiration des retenues de coupon : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
                else if (!resultat.Value.IsEmpty)
                {
                    // Le VOLUME d'abord : c'est lui qui dit combien d'enveloppe
                    // dormait.
                    _logger.LogInformation(
                        "Expiration des retenues de coupon : {Budget} unite(s) de budget rendue(s), "
                        + "{Reservations} retenue(s) expiree(s) sur {Coupons} coupon(s).",
                        resultat.Value.Budget, resultat.Value.Reservations, resultat.Value.Coupons);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Un incident de base ne doit jamais tuer le travailleur : le tour
                // suivant reprendra les mêmes retenues, rien n'est perdu — c'est
                // précisément ce que l'idempotence du balayage garantit.
                _logger.LogError(ex, "Expiration des retenues de coupon : echec du cycle.");
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
