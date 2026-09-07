using HBA.Shared.Infrastructure.Persistence;
using HBA.Communication.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using HBA.Communication.Infrastructure.Messaging.Kafka.Retry;
using HBA.Communication.Infrastructure.Messaging.Kafka.Processors;
// COPIE DEPUIS `HBA.Shared.Infrastructure.Outbox`.

namespace HBA.Communication.Infrastructure.Persistence.Outbox;

/// <summary>Efface les messages d'outbox déjà traités, passé un délai de rétention.</summary>
public sealed class OutboxPurger : BackgroundService
{
    /// <summary>Cadence : une passe par heure.</summary>
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(1);

    /// <summary>
    /// Plafond par passe. Un <c> DELETE</c> non borné sur une table qui n'a jamais
    /// été purgée peut porter sur des millions de lignes : il tiendrait un verrou
    /// long, gonflerait le WAL, et retarderait le processeur d'outbox qui écrit
    /// dans la même table.
    /// </summary>
    private const int TaillePasse = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPurger> _logger;

    public OutboxPurger(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxPurger> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Durée de rétention des messages traités.</summary>
    internal static TimeSpan Retention
    {
        get
        {
            var brut = Environment.GetEnvironmentVariable("OUTBOX_RETENTION_DAYS");

            return int.TryParse(brut, out var jours) && jours > 0
                ? TimeSpan.FromDays(jours)
                : TimeSpan.FromDays(7);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var module = typeof(MessagingDbContext).Name.Replace("DbContext", string.Empty);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Arrêt normal de l'hôte : on sort sans bruit.
                return;
            }

            try
            {
                var effaces = await PurgerAsync(stoppingToken);

                if (effaces > 0)
                {
                    _logger.LogInformation(
                        "Outbox {Module} : {Effaces} message(s) traité(s) effacé(s) (rétention {Jours} j).",
                        module, effaces, Retention.TotalDays);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // ON N'INTERROMPT PAS LA BOUCLE. Une purge qui échoue est un
                // désagrément ; un service hôte qui s'arrête parce que sa purge a
                // échoué est une panne.
                _logger.LogError(
                    ex, "Outbox {Module} : échec de la purge. Nouvelle tentative dans {Intervalle}.",
                    module, Intervalle);
            }
        }
    }

    private async Task<int> PurgerAsync(CancellationToken cancellationToken)
    {
        var limite = DateTime.UtcNow - Retention;
        var total = 0;

        // Par tranches, jusqu'à ce qu'il ne reste rien à effacer — ou que l'hôte
        // s'arrête.
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();

            var identifiants = await dbContext.OutboxMessages
                .Where(m => m.ProcessedOnUtc != null
                            && m.ProcessedOnUtc < limite
                            && m.DeadLetteredOnUtc == null)
                .OrderBy(m => m.OccurredOnUtc)
                .Select(m => m.Id)
                .Take(TaillePasse)
                .ToListAsync(cancellationToken);

            if (identifiants.Count == 0)
            {
                break;
            }

            var effaces = await dbContext.OutboxMessages
                .Where(m => identifiants.Contains(m.Id))
                .ExecuteDeleteAsync(cancellationToken);

            total += effaces;

            if (identifiants.Count < TaillePasse)
            {
                break;
            }
        }

        return total;
    }
}
