using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Contracts;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// Applique un webhook de DÉPÔT (payout) au retrait correspondant : confirmation en
/// temps réel, là où la réconciliation attendrait jusqu'à deux minutes.
/// </summary>
public sealed record ApplyPayoutWebhookCommand(string ProviderReference, PayoutProgress Progress) : ICommand;

internal sealed class ApplyPayoutWebhookCommandHandler : ICommandHandler<ApplyPayoutWebhookCommand>
{
    private readonly IWithdrawalRepository _withdrawals;
    private readonly WithdrawalSettlement _settlement;
    private readonly IWalletUnitOfWork _unitOfWork;

    public ApplyPayoutWebhookCommandHandler(
        IWithdrawalRepository withdrawals,
        WithdrawalSettlement settlement,
        IWalletUnitOfWork unitOfWork)
    {
        _withdrawals = withdrawals;
        _settlement = settlement;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApplyPayoutWebhookCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.ProviderReference))
        {
            return Result.Success(); // rien à corréler : on acquitte.
        }

        var withdrawal = await _withdrawals.GetByProviderRefAsync(command.ProviderReference, cancellationToken);
        if (withdrawal is null)
        {
            // Dépôt inconnu (retrait pas encore commité, ou versement hors
            // marketplace) : on acquitte pour ne pas déclencher les renvois en
            // boucle du PSP. La réconciliation périodique tranchera de toute façon
            // ce retrait.
            return Result.Success();
        }

        if (await _settlement.ApplyAsync(withdrawal, command.Progress, cancellationToken))
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
