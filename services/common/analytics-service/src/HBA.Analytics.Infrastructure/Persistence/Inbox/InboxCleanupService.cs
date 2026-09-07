using HBA.Analytics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Analytics.Infrastructure.Persistence.Inbox;

/// <summary>Purge les traces d'idempotence de l'inbox.</summary>
internal sealed class InboxCleanupService : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Intervalle = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _fabrique;
    private readonly ILogger<InboxCleanupService> _journal;

    public InboxCleanupService(IServiceScopeFactory fabrique, ILogger<InboxCleanupService> journal)
    {
        _fabrique = fabrique;
        _journal = journal;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var portee = _fabrique.CreateScope();
                var contexte = portee.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

                var limite = DateTime.UtcNow - Retention;

                var supprimees = await contexte.Set<ConsumerInboxEntry>()
                    .Where(entree => entree.ProcessedAtUtc < limite)
                    .ExecuteDeleteAsync(stoppingToken);

                if (supprimees > 0)
                {
                    _journal.LogInformation(
                        "Inbox : {Supprimees} traces de plus de {Jours} jours purgees.",
                        supprimees, Retention.TotalDays);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // UNE PURGE QUI ECHOUE NE DOIT PAS ARRETER L'HOTE. La table grossit
                // un tour de plus ; l'alternative — laisser l'exception remonter —
                // arreterait un service qui sert parfaitement ses requetes.
                _journal.LogError(exception, "Inbox : la purge a echoue, elle sera retentee.");
            }

            try
            {
                await Task.Delay(Intervalle, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
