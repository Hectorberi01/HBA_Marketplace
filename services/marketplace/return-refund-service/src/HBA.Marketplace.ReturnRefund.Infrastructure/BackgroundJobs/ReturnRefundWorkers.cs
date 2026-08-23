using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.Commands.ExpireReturns;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.BackgroundJobs;

// ═════════════════════════════════════════════════════════════════════════════
// LES TROIS TRAVAILLEURS DE CE FICHIER ÉTAIENT DES COQUILLES.
//
// CHACUN JOURNALISAIT SON NOM ET RENDAIT LA MAIN :
//
//     protected override Task ExecuteAsync(CancellationToken stoppingToken)
//     {
//         _logger.LogInformation("RefundRetryWorker active.");
//         return Task.CompletedTask;
//     }
//
// « active » : le mot est exact et le sens est faux. Le service hôte les
// enregistrait, la ligne apparaissait au démarrage, et l'exploitation lisait
// « les trois travailleurs tournent ». Aucun ne faisait quoi que ce soit.
//
// C'est la panne la plus coûteuse de ce module : `ExecuteRefundCommand` n'avait
// AUCUN émetteur, donc aucune décision de remboursement n'aboutissait jamais, et
// le journal de démarrage disait le contraire.
//
// `OutboxPublisherWorker` a été SUPPRIMÉ plutôt qu'implémenté — voir
// `ReturnRefundModuleInstaller`.
//
// UNE SEULE INSTANCE. Comme l'outbox (`OutboxRegistration`), ces balayages ne
// posent pas de `SELECT … FOR UPDATE SKIP LOCKED` : deux répliques liraient les
// mêmes lignes. Le jeton de concurrence de l'agrégat (`Version`) empêche le
// double VERSEMENT — le second exécutant échoue à la réservation — mais pas le
// travail en double. Avant de mettre l'API à l'échelle horizontale, il faut le
// verrou de ligne.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ferme les dossiers dont la fenêtre de retour est dépassée.
/// </summary>
internal sealed class ExpireReturnsWorker : BackgroundService
{
    // Une expiration n'a aucune urgence : le dossier est déjà hors délai depuis
    // des heures quand on le voit. Balayer souvent ne rendrait service à personne
    // et relirait la table pour rien.
    private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(10);
    private const int TailleLot = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpireReturnsWorker> _logger;

    public ExpireReturnsWorker(IServiceScopeFactory scopeFactory, ILogger<ExpireReturnsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(new ExpireReturnsCommand(TailleLot), stoppingToken);
                if (resultat.IsSuccess && resultat.Value > 0)
                {
                    _logger.LogInformation("Expiration des retours : {Nombre} dossier(s) clos.", resultat.Value);
                }
                else if (resultat.IsFailure)
                {
                    _logger.LogError(
                        "Expiration des retours : {Code} — {Message}",
                        resultat.Error.Code, resultat.Error.Message);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Un incident de base ne doit jamais tuer le travailleur : le tour
                // suivant reprendra les mêmes dossiers, rien n'est perdu.
                _logger.LogError(ex, "Expiration des retours : echec du cycle.");
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

/// <summary>
/// Le raccord entre la DÉCISION de remboursement et son EXÉCUTION.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST CE BALAYAGE QUI FAIT PARTIR L'ARGENT.
///
/// `DecideRefundCommandHandler` écrit un `Refund` en `Pending` et commite. Ce
/// travailleur relit ce qui est COMMITTÉ et envoie `ExecuteRefundCommand`. Rien
/// d'autre ne relie les deux — avant ce lot, rien ne les reliait du tout, et
/// `ReturnStatus.Refunded` était inatteignable.
///
/// Il reprend trois statuts, et chacun pour une raison distincte :
///
///   • `Pending`    — décidé, jamais tenté. Le cas normal.
///   • `Processing` — tenté, issue inconnue : le processus est tombé entre la
///                    réservation et la réponse du prestataire. Sans reprise, ce
///                    dossier n'est plus relancé par personne. La clé
///                    d'idempotence rend l'appel sûr.
///   • `Failed`     — tenté, refusé. Réessayé jusqu'à `MaxTentatives`, après quoi
///                    `ExecuteRefundCommandHandler` bascule le dossier en
///                    `ManualReview` et cesse de le reprendre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class RefundRetryWorker : BackgroundService
{
    // COURT, ET ASSUMÉ. Ce délai est celui que le client passe à attendre son
    // argent après que le vendeur a validé. C'est aussi la latence maximale entre
    // une décision et le message « versement effectué ».
    private static readonly TimeSpan Intervalle = TimeSpan.FromSeconds(20);
    private const int TailleLot = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefundRetryWorker> _logger;

    public RefundRetryWorker(IServiceScopeFactory scopeFactory, ILogger<RefundRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalle);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BalayerAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Execution des remboursements : echec du cycle.");
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

    private async Task BalayerAsync(CancellationToken stoppingToken)
    {
        // UNE PORTÉE POUR LA SÉLECTION, UNE PORTÉE PAR REMBOURSEMENT.
        //
        // Chaque exécution a son propre `DbContext`, donc son propre suivi
        // d'entités et son propre jeton de concurrence. Partager une portée ferait
        // qu'un remboursement en échec laisserait ses entités modifiées dans le
        // contexte du suivant — et le `SaveChanges` du suivant les committerait.
        List<RefundExecutionTicket> tickets;

        using (var scope = _scopeFactory.CreateScope())
        {
            var returns = scope.ServiceProvider.GetRequiredService<IReturnRequestRepository>();
            tickets = (await returns.ListRefundsAwaitingExecutionAsync(TailleLot, stoppingToken)).ToList();
        }

        if (tickets.Count == 0)
        {
            return;
        }

        foreach (var ticket in tickets)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var resultat = await sender.Send(
                    new ExecuteRefundCommand(ticket.ReturnId, ticket.RefundId), stoppingToken);

                if (resultat.IsFailure)
                {
                    // JOURNALISÉ EN ERREUR, JAMAIS AVALÉ. Un remboursement qui
                    // n'aboutit pas est de l'argent qu'un client attend : le tour
                    // suivant réessaiera, et la trace dit à quoi il se heurte.
                    _logger.LogError(
                        "Remboursement {RefundId} du retour {ReturnId} non execute : {Code} — {Message}",
                        ticket.RefundId, ticket.ReturnId, resultat.Error.Code, resultat.Error.Message);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Inclut `DbUpdateConcurrencyException` : un autre exécutant a
                // réservé ce remboursement entre la sélection et l'écriture. Ce
                // n'est pas un incident — c'est le verrou qui fait son travail.
                _logger.LogWarning(
                    ex,
                    "Remboursement {RefundId} du retour {ReturnId} : cycle interrompu, reprise au prochain tour.",
                    ticket.RefundId, ticket.ReturnId);
            }
        }
    }
}
