using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Payments.Contracts;
using HBA.Merchants.Contracts;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Earnings;
using HBA.Financial.Wallet.Contracts;
using HBA.Financial.Wallet.Domain.Wallets;
using Microsoft.Extensions.Logging;

namespace HBA.Financial.Wallet.Application.Wallets;

// Nouveau flux de retrait : 1.

/// <summary>
/// Demande de retrait d'un vendeur : retient les fonds (débit du solde principal)
/// et crée une demande en attente de validation admin.
/// </summary>
public sealed record RequestWithdrawalCommand(Guid SellerId, decimal Amount) : ICommand<WithdrawalView>;

internal sealed class RequestWithdrawalCommandHandler : ICommandHandler<RequestWithdrawalCommand, WithdrawalView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISellerModuleApi _sellers;
    private readonly SellerEarningImputation _imputation;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<RequestWithdrawalCommandHandler> _logger;

    public RequestWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        ISellerModuleApi sellers,
        SellerEarningImputation imputation,
        IWalletUnitOfWork unitOfWork,
        ILogger<RequestWithdrawalCommandHandler> logger)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _sellers = sellers;
        _imputation = imputation;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<WithdrawalView>> Handle(RequestWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var wallet = await _wallets.GetBySellerAsync(command.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        // `GetSellerPayoutAsync`, ET SURTOUT PAS `GetSellerAsync().Payout`.
        var payout = await _sellers.GetSellerPayoutAsync(command.SellerId, cancellationToken);

        // « VENDEUR INCONNU » N'EST PAS « VENDEUR SANS COMPTE ».
        if (!payout.SellerExists)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound(
                "wallet.seller_not_found", "Vendeur introuvable."));
        }

        var account = payout.Account;
        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.no_payout_account", "Aucun compte de versement Mobile Money configuré."));
        }

        // Débite le solde principal (échoue si montant invalide / solde
        // insuffisant).
        var debit = wallet.Withdraw(command.Amount);
        if (debit.IsFailure)
        {
            return Result.Failure<WithdrawalView>(debit.Error);
        }

        var currency = wallet.Currency;

        // LA DESTINATION EST FIGÉE ICI, PAS RELUE À L'APPROBATION.
        var withdrawal = Withdrawal.Create(
            command.SellerId, command.Amount, currency,
            account.Provider, account.AccountNumber, account.AccountName);
        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            command.SellerId, WalletAccount.Available, WalletDirection.Debit, command.Amount, currency,
            "withdrawal_request", "withdrawal", withdrawal.Id.Value), cancellationToken);

        // SANS CETTE LIGNE, LE GAIN RETIRÉ ICI SERA RE-VERSÉ PAR LE PROCHAIN LOT.
        var reliquat = await _imputation.ImputeWithdrawalAsync(
            command.SellerId, command.Amount, withdrawal.Id.Value, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (reliquat > 0m)
        {
            _logger.LogInformation(
                "Retrait {WithdrawalId} du vendeur {SellerId} : {Reliquat} {Currency} non couverts par un gain entier. "
                + "Le lot de reversement suivant sera plafonné d'autant.",
                withdrawal.Id, command.SellerId, reliquat, currency);
        }

        return WalletMapper.ToView(withdrawal);
    }
}

/// <summary>
/// Validation admin d'une demande de retrait : déclenche le payout FedaPay Mobile
/// Money.
/// </summary>
public sealed record ApproveWithdrawalCommand(Guid WithdrawalId) : ICommand<WithdrawalView>;

internal sealed class ApproveWithdrawalCommandHandler : ICommandHandler<ApproveWithdrawalCommand, WithdrawalView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly ISellerModuleApi _sellers;
    private readonly IPayoutModuleApi _payouts;
    private readonly SellerEarningImputation _imputation;
    private readonly IWalletUnitOfWork _unitOfWork;

    public ApproveWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        ISellerModuleApi sellers,
        IPayoutModuleApi payouts,
        SellerEarningImputation imputation,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _sellers = sellers;
        _payouts = payouts;
        _imputation = imputation;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(ApproveWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(new WithdrawalId(command.WithdrawalId), cancellationToken);
        if (withdrawal is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.withdrawal_not_found", "Demande de retrait introuvable."));
        }

        if (!withdrawal.IsPendingApproval)
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.withdrawal_not_pending", "Cette demande a déjà été traitée."));
        }

        var wallet = await _wallets.GetBySellerAsync(withdrawal.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        // MÊME CORRECTION QU'À LA DEMANDE, ET ELLE COMPTAIT ENCORE PLUS ICI.
        var payout = await _sellers.GetSellerPayoutAsync(withdrawal.SellerId, cancellationToken);
        var account = payout.Account;
        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return await FailAndRefundAsync(
                withdrawal, wallet,
                payout.SellerExists
                    ? "Aucun compte de versement Mobile Money configuré."
                    : "Vendeur introuvable : la demande ne peut pas être payée.",
                cancellationToken);
        }

        // ON PAIE LA DESTINATION FIGÉE À LA DEMANDE — ET ON REFUSE SI ELLE A BOUGÉ.
        if (withdrawal.HasFrozenDestination
            && !withdrawal.MatchesDestination(account.Provider, account.AccountNumber))
        {
            return await FailAndRefundAsync(
                withdrawal, wallet,
                "Le compte de versement a changé depuis la demande. "
                + "La demande est annulée et les fonds recrédités : le vendeur doit en refaire une.",
                cancellationToken);
        }

        // Les demandes créées AVANT l'existence de la destination figée n'en ont
        // pas.
        var msisdn = withdrawal.PayoutAccountNumber ?? account.AccountNumber;
        var provider = withdrawal.PayoutProvider ?? account.Provider;
        var beneficiaire = withdrawal.PayoutAccountName ?? account.AccountName;

        // LE NOM DE BOUTIQUE N'EST LU QUE SI LE BÉNÉFICIAIRE MANQUE.
        if (string.IsNullOrWhiteSpace(beneficiaire))
        {
            var seller = await _sellers.GetSellerAsync(withdrawal.SellerId, cancellationToken);
            beneficiaire = seller?.ShopName ?? string.Empty;
        }

        var outcome = await _payouts.SendMobileMoneyPayoutAsync(
            new PayoutInstructionContract(
                Amount: withdrawal.Amount,
                Currency: withdrawal.Currency,
                BeneficiaryName: beneficiaire,
                Msisdn: msisdn,
                // L'opérateur du vendeur détermine à lui seul le routage PSP (mode
                // ET pays).
                Provider: account.Provider,
                Reference: $"withdrawal:{withdrawal.Id}"),
            cancellationToken);

        switch (outcome.Status)
        {
            // Rejet DÉFINITIF du PSP : rien n'est parti → on peut recréditer sans
            // risque.
            case PayoutOutcomeStatus.Failed:
                return await FailAndRefundAsync(withdrawal, wallet, outcome.Error ?? "Échec du versement.", cancellationToken);

            // Issue INDÉTERMINÉE (timeout, 5xx…) : le versement est peut-être
            // parti.
            case PayoutOutcomeStatus.Unknown:
                withdrawal.MarkProcessing(outcome.ProviderReference, outcome.Error);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);

            // Accepté = créé + démarré chez FedaPay.
            default:
                withdrawal.MarkProcessing(outcome.ProviderReference);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);
        }
    }

    // Échec du payout après retenue des fonds : marque Failed et recrédite le
    // solde.
    private async Task<Result<WithdrawalView>> FailAndRefundAsync(
        Withdrawal withdrawal, SellerWallet wallet, string reason, CancellationToken ct)
    {
        withdrawal.Fail(reason);
        wallet.CreditAvailable(withdrawal.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            withdrawal.SellerId, WalletAccount.Available, WalletDirection.Credit, withdrawal.Amount, withdrawal.Currency,
            "withdrawal_refund", "withdrawal", withdrawal.Id.Value), ct);

        // Le solde revient, les gains aussi : sinon le vendeur récupère son argent
        // mais ses gains restent soldés, n'entrent plus dans aucun lot, et son
        // solde ne redescend jamais.
        await _imputation.ReleaseWithdrawalAsync(withdrawal.Id.Value, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Failure<WithdrawalView>(Error.Failure("wallet.withdrawal_failed", reason));
    }
}

/// <summary>Refus admin d'une demande de retrait : recrédite les fonds retenus.</summary>
public sealed record RejectWithdrawalCommand(Guid WithdrawalId, string Reason) : ICommand<WithdrawalView>;

internal sealed class RejectWithdrawalCommandHandler : ICommandHandler<RejectWithdrawalCommand, WithdrawalView>
{
    private readonly ISellerWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly SellerEarningImputation _imputation;
    private readonly IWalletUnitOfWork _unitOfWork;

    public RejectWithdrawalCommandHandler(
        ISellerWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        SellerEarningImputation imputation,
        IWalletUnitOfWork unitOfWork)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _imputation = imputation;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<WithdrawalView>> Handle(RejectWithdrawalCommand command, CancellationToken cancellationToken)
    {
        var withdrawal = await _withdrawals.GetByIdAsync(new WithdrawalId(command.WithdrawalId), cancellationToken);
        if (withdrawal is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.withdrawal_not_found", "Demande de retrait introuvable."));
        }

        if (!withdrawal.IsPendingApproval)
        {
            return Result.Failure<WithdrawalView>(Error.Validation(
                "wallet.withdrawal_not_pending", "Cette demande a déjà été traitée."));
        }

        var wallet = await _wallets.GetBySellerAsync(withdrawal.SellerId, cancellationToken);
        if (wallet is null)
        {
            return Result.Failure<WithdrawalView>(Error.NotFound("wallet.not_found", "Aucun portefeuille pour ce vendeur."));
        }

        wallet.CreditAvailable(withdrawal.Amount);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            withdrawal.SellerId, WalletAccount.Available, WalletDirection.Credit, withdrawal.Amount, withdrawal.Currency,
            "withdrawal_reject", "withdrawal", withdrawal.Id.Value), cancellationToken);

        // Les gains imputés à cette demande redeviennent payables : ils entreront
        // dans un prochain lot, ou dans la prochaine demande du vendeur.
        await _imputation.ReleaseWithdrawalAsync(withdrawal.Id.Value, cancellationToken);

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Demande refusée par l'administrateur." : command.Reason.Trim();
        withdrawal.Reject(reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletMapper.ToView(withdrawal);
    }
}

/// <summary>Petit utilitaire partagé pour identifier un compte de versement Mobile Money.</summary>
internal static class WalletPayout
{
    /// <summary>
    /// Opérateurs que l'on sait RÉELLEMENT reverser via FedaPay (mode + pays
    /// connus).
    /// </summary>
    public static bool IsMobileMoney(string provider)
        => provider.ToLowerInvariant() is "mtnmomo" or "moovmoney" or "celtis";
}
