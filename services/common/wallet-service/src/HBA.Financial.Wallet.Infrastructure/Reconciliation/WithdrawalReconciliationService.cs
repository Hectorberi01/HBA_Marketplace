using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HBA.Financial.Wallet.Application.Wallets;

namespace HBA.Financial.Wallet.Infrastructure.Reconciliation;

/// <summary>
/// Réconcilie périodiquement les retraits « en cours » avec le statut réel du dépôt
/// chez le PSP (FedaPay).
/// </summary>
public sealed class WithdrawalReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WithdrawalReconciliationService> _logger;

    public WithdrawalReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<WithdrawalReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var result = await sender.Send(new ReconcileWithdrawalsCommand(BatchSize), stoppingToken);
                if (result.IsSuccess && result.Value > 0)
                {
                    _logger.LogInformation("Réconciliation des retraits : {Count} clôturé(s).", result.Value);
                }

                // Même filet de sécurité pour les remboursements CLIENT « en cours
                // » : un payout FedaPay accepté n'est que « started » ; ce balayage
                // clôture en Completed (statut « sent ») ou contre-passe le débit
                // plateforme (statut « failed »).
                var refunds = await sender.Send(new ReconcileCustomerRefundsCommand(BatchSize), stoppingToken);
                if (refunds.IsSuccess && refunds.Value > 0)
                {
                    _logger.LogInformation("Réconciliation des remboursements client : {Count} clôturé(s).", refunds.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // Une panne du PSP ne doit jamais tuer le service : on réessaiera.
                _logger.LogError(ex, "Réconciliation des retraits : échec du cycle.");
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
