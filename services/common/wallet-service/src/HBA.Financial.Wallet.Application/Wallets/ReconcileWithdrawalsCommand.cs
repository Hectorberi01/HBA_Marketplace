using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Contracts;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// Réconcilie les retraits « en cours » avec le statut RÉEL du dépôt chez le PSP.
/// </summary>
public sealed record ReconcileWithdrawalsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ReconcileWithdrawalsCommandHandler : ICommandHandler<ReconcileWithdrawalsCommand, int>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IPayoutModuleApi _payouts;
    private readonly WithdrawalSettlement _settlement;
    private readonly IWalletUnitOfWork _unitOfWork;

    public ReconcileWithdrawalsCommandHandler(
        IWithdrawalRepository withdrawals,
        IPayoutModuleApi payouts,
        WithdrawalSettlement settlement,
        IWalletUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _payouts = payouts;
        _settlement = settlement;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(ReconcileWithdrawalsCommand command, CancellationToken cancellationToken)
    {
        var pending = await _withdrawals.ListProcessingForReconciliationAsync(command.BatchSize, cancellationToken);
        var settled = 0;

        foreach (var withdrawal in pending)
        {
            // Sans référence PSP, on ne peut RIEN interroger : le dépôt a peut-être
            // été créé sans qu'on récupère son identifiant (timeout).
            if (string.IsNullOrWhiteSpace(withdrawal.ProviderRef))
            {
                continue;
            }

            var progress = await _payouts.GetPayoutProgressAsync(withdrawal.ProviderRef!, cancellationToken);

            if (await _settlement.ApplyAsync(withdrawal, progress, cancellationToken))
            {
                settled++;
            }
        }

        if (settled > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return settled;
    }
}
