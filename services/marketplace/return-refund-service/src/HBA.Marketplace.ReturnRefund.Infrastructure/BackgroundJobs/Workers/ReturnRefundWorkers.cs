using HBA.Marketplace.ReturnRefund.Application.Commands;
using HBA.Marketplace.ReturnRefund.Application.Commands.ExpireReturns;
using HBA.Marketplace.ReturnRefund.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HBA.Marketplace.ReturnRefund.Infrastructure.BackgroundJobs;

// LES TROIS TRAVAILLEURS DE CE FICHIER ÉTAIENT DES COQUILLES.

/// <summary>Ferme les dossiers dont la fenêtre de retour est dépassée.</summary>
internal sealed class ExpireReturnsWorker : BackgroundService
{
    // Une expiration n'a aucune urgence : le dossier est déjà hors délai depuis des
    // heures quand on le voit.
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

/// <summary>Le raccord entre la DÉCISION de remboursement et son EXÉCUTION.</summary>
internal sealed class RefundRetryWorker : BackgroundService
{
    // COURT, ET ASSUMÉ. Ce délai est celui que le client passe à attendre son
    // argent après que le vendeur a validé.
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
                    // JOURNALISÉ EN ERREUR, JAMAIS AVALÉ.
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
                // réservé ce remboursement entre la sélection et l'écriture.
                _logger.LogWarning(
                    ex,
                    "Remboursement {RefundId} du retour {ReturnId} : cycle interrompu, reprise au prochain tour.",
                    ticket.RefundId, ticket.ReturnId);
            }
        }
    }
}
