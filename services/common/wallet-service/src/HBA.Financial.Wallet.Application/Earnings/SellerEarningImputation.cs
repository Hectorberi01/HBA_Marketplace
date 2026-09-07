using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>QUELS GAINS UN RETRAIT CONSOMME-T-IL ? — LE MAILLON QUI N'EXISTAIT PAS.</summary>
public sealed class SellerEarningImputation
{
    private readonly ISellerEarningRepository _earnings;

    public SellerEarningImputation(ISellerEarningRepository earnings) => _earnings = earnings;

    /// <summary>RÈGLE D'IMPUTATION : LE PLUS ANCIEN D'ABORD (PEPS / FIFO).</summary>
    /// <returns>Le RELIQUAT : la part du retrait qu'aucun gain entier ne couvre.</returns>
    public async Task<decimal> ImputeWithdrawalAsync(
        Guid sellerId, decimal amount, Guid withdrawalId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        var payables = await _earnings.ListReleasedBySellerAsync(sellerId, cancellationToken);

        // LE NET RESTANT, PAS LE NET D'ORIGINE — SUR LES TROIS LIGNES CI-DESSOUS.
        var impute = 0m;
        foreach (var earning in payables)
        {
            if (earning.RemainingNetAmount <= 0m)
            {
                // Un gain nul ne consomme rien mais doit sortir de la file payable
                // : le laisser « Released » le ferait revenir à chaque imputation,
                // et il finirait dans un lot pour un versement de zéro franc.
                earning.MarkSettledByWithdrawal(withdrawalId);
                continue;
            }

            if (impute + earning.RemainingNetAmount > amount)
            {
                // ON S'ARRÊTE, ON NE SAUTE PAS AU SUIVANT.
                break;
            }

            if (earning.MarkSettledByWithdrawal(withdrawalId))
            {
                impute += earning.RemainingNetAmount;
            }
        }

        return amount - impute;
    }

    /// <summary>
    /// Rend payables les gains qu'un retrait avait consommés : refus admin, rejet
    /// du prestataire, échec confirmé à la réconciliation.
    /// </summary>
    /// <returns>Le nombre de gains rendus payables (journalisation).</returns>
    public async Task<int> ReleaseWithdrawalAsync(Guid withdrawalId, CancellationToken cancellationToken = default)
    {
        var imputes = await _earnings.ListByWithdrawalAsync(withdrawalId, cancellationToken);

        var liberes = 0;
        foreach (var earning in imputes)
        {
            if (earning.Status == EarningStatus.Settled)
            {
                earning.Unsettle();
                liberes++;
            }
        }

        return liberes;
    }
}
