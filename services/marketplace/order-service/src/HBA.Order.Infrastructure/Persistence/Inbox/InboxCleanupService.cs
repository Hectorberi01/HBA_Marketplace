using HBA.Orders.Infrastructure.Persistence.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Orders.Infrastructure.Persistence.Inbox;

/// <summary>
/// Purge les traces d'idempotence de l'inbox.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE SERVICE N'EXISTAIT NULLE PART, ET C'EST UN VRAI MANQUE.
///
/// `OutboxPurger` existait. `IdempotencyPurger` existait. RIEN ne purgeait
/// `consumer_inbox` : chaque evenement traite y laissait une ligne, definitivement,
/// dans les quinze services qui consomment.
///
/// Ce n'est pas urgent — quelques centaines de milliers de lignes par an — mais
/// c'est une table qui ne cesse jamais de grossir, indexee sur une cle CONSULTEE A
/// CHAQUE MESSAGE RECU. Elle finira par couter sur le chemin chaud, et ce jour-la
/// personne ne cherchera la.
///
/// LA RETENTION EST LONGUE, ET C'EST DELIBERE. Une trace d'inbox est ce qui
/// empeche un evenement d'etre retraite. La supprimer trop tot ne casse rien
/// tant que le message ne revient pas — et quand il revient, l'effet metier est
/// rejoue sans que rien ne le signale. Trente jours couvre largement une
/// remise a zero d'offsets, qui est le seul cas ou un message ancien revient.
///
/// CE QUE ÇA NE COUVRE PAS : un rejeu deliberement plus ancien que la retention.
/// Avant une remise a zero d'offsets au-dela de trente jours, il faut savoir que
/// les gestionnaires non idempotents par eux-memes refont leur effet.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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
                var contexte = portee.ServiceProvider.GetRequiredService<OrderingDbContext>();

                var limite = DateTime.UtcNow - Retention;

                var supprimees = await contexte.Set<ConsumerInboxEntry>()
                    .Where(entree => entree.ProcessedOnUtc < limite)
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
