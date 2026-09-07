using HBA.Shared.Infrastructure.Idempotency;
using HBA.Shared.Infrastructure.Persistence;
using HBA.Promotions.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HBA.Promotions.Infrastructure.Persistence.Outbox;
using HBA.Promotions.Infrastructure.Persistence.Inbox;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Retry;
using HBA.Promotions.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Idempotency`.

namespace HBA.Promotions.Infrastructure.Idempotency;

/// <summary>Efface les réservations d'idempotence dont l'échéance est passée.</summary>
public sealed class IdempotencyPurger : BackgroundService
{
    /// <summary>Une passe par heure, comme l'outbox.</summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(1);

    /// <summary>
    /// Plafond par passe. Même raisonnement que <c> OutboxPurger</c> : un <c>
    /// DELETE</c> non borné sur une table jamais purgée tiendrait un verrou long et
    /// gonflerait le WAL.
    /// </summary>
    private const int TaillePasse = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyPurger> _logger;

    public IdempotencyPurger(
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyPurger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var module = typeof(PromotionsDbContext).Name.Replace("DbContext", string.Empty);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Le premier passage a lieu APRÈS le délai, pas au démarrage : au
                // boot, l'hôte a mieux à faire que d'ouvrir une transaction de
                // suppression.
                await Task.Delay(Intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var effaces = await PurgerAsync(stoppingToken);

                if (effaces > 0)
                {
                    _logger.LogInformation(
                        "Idempotence {Module} : {Effaces} réservation(s) périmée(s) effacée(s).",
                        module, effaces);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // ON N'INTERROMPT PAS LA BOUCLE. Une purge qui échoue est une table
                // qui grossit, pas un service en panne : sortir ici transformerait
                // un incident de stockage en perte définitive du ménage, jusqu'au
                // prochain redémarrage.
                _logger.LogError(
                    ex, "Idempotence {Module} : échec de la purge. Nouvelle tentative dans {Intervalle}.",
                    module, Intervalle);
            }
        }
    }

    private async Task<int> PurgerAsync(CancellationToken cancellationToken)
    {
        var total = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PromotionsDbContext>();

            // MAINTENANT EST RELU À CHAQUE TRANCHE, et non figé avant la boucle :
            // une purge longue ne doit pas laisser derrière elle les lignes qui ont
            // expiré pendant qu'elle tournait.
            var limite = DateTime.UtcNow;

            var perimees = dbContext.Set<IdempotencyRecord>()
                .Where(r => r.ExpiresAtUtc < limite);

            var borne = await perimees
                .OrderBy(r => r.ExpiresAtUtc)
                .Select(r => r.ExpiresAtUtc)
                .Skip(TaillePasse - 1)
                .Take(1)
                .FirstOrDefaultAsync(cancellationToken);

            // Moins d'une tranche entière reste : on efface le reliquat et on sort.
            if (borne == default)
            {
                total += await perimees.ExecuteDeleteAsync(cancellationToken);
                return total;
            }

            var effaces = await dbContext.Set<IdempotencyRecord>()
                .Where(r => r.ExpiresAtUtc <= borne)
                .ExecuteDeleteAsync(cancellationToken);

            // CEINTURE. Si une passe n'efface rien alors qu'une borne a été
            // trouvée, c'est qu'une autre instance a vidé la tranche entre les deux
            // requêtes.
            if (effaces == 0)
            {
                return total;
            }

            total += effaces;
        }

        return total;
    }
}
