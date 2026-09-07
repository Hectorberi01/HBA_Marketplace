using HBA.Financial.Payments.Contracts;
using HBA.Financial.Wallet.Application.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;

namespace HBA.Financial.Wallet.Application.Wallets;

/// <summary>
/// Applique à un retrait le statut RÉEL de son dépôt chez le PSP. Point d'entrée
/// UNIQUE, partagé par les deux sources de vérité : • le webhook (temps réel, mais
/// peut se perdre ou arriver en désordre) ; • la réconciliation périodique (lente,
/// mais exhaustive — le filet de sécurité).
/// </summary>
public sealed class WithdrawalSettlement
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWalletTransactionRepository _ledger;
    private readonly SellerEarningImputation _imputation;

    public WithdrawalSettlement(
        ISellerWalletRepository wallets,
        IWalletTransactionRepository ledger,
        SellerEarningImputation imputation)
    {
        _wallets = wallets;
        _ledger = ledger;
        _imputation = imputation;
    }

    /// <summary>
    /// Renvoie <c> true</c> si le retrait a été tranché (clôturé ou remboursé) par
    /// cet appel.
    /// </summary>
    public async Task<bool> ApplyAsync(Withdrawal withdrawal, PayoutProgress progress, CancellationToken cancellationToken = default)
    {
        if (!withdrawal.IsProcessing)
        {
            return false;
        }

        switch (progress)
        {
            // Seule preuve de versement : c'est l'unique chemin vers Completed.
            case PayoutProgress.Sent:
                withdrawal.Complete(withdrawal.ProviderRef);
                return true;

            // Échec CONFIRMÉ par le PSP : l'argent n'est pas parti → on recrédite.
            case PayoutProgress.Failed:
                await RefundAsync(withdrawal, cancellationToken);
                return true;

            // pending / started / processing / unknown : encore en vol, ou PSP
            // muet.
            default:
                return false;
        }
    }

    /// <summary>Échec confirmé : le retrait est marqué Failed et les fonds recrédités.</summary>
    private async Task RefundAsync(Withdrawal withdrawal, CancellationToken cancellationToken)
    {
        withdrawal.Fail("Versement refusé par le prestataire (statut « failed »).");

        var wallet = await _wallets.GetBySellerAsync(withdrawal.SellerId, cancellationToken);
        if (wallet is null)
        {
            // Portefeuille introuvable : on garde la trace de l'échec, sans crédit
            // fantôme.
            return;
        }

        wallet.CreditAvailable(withdrawal.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            withdrawal.SellerId, WalletAccount.Available, WalletDirection.Credit,
            withdrawal.Amount, withdrawal.Currency,
            "withdrawal_refund", "withdrawal", withdrawal.Id.Value), cancellationToken);

        // Troisième et dernier chemin de remboursement (les deux autres sont dans
        // WalletCommands).
        await _imputation.ReleaseWithdrawalAsync(withdrawal.Id.Value, cancellationToken);
    }
}
