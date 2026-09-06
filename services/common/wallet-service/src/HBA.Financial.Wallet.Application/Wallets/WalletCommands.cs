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

// ============================================================================
// Nouveau flux de retrait :
//   1. Le vendeur DEMANDE un retrait → les fonds sont retenus (débités du solde
//      principal) et la demande est créée à l'état « Requested ». Aucun payout.
//   2. L'ADMIN valide (ApproveWithdrawal) → payout FedaPay réel → Completed, ou
//      refuse (RejectWithdrawal) → les fonds sont recrédités.
//
// ════════════════════════════════════════════════════════════════════════════
// CE CANAL IGNORAIT TOTALEMENT L'EXISTENCE DES `SellerEarning`.
//
// Il débitait le portefeuille, envoyait un versement Mobile Money réel, et ne
// touchait JAMAIS au statut des gains. Le lot de reversement, lui, prenait tous
// les gains « Released » et ne touchait JAMAIS au portefeuille. Un même gain
// pouvait donc être encaissé par retrait — argent réellement parti — PUIS
// re-versé dans un lot. Aucun des deux canaux ne voyait l'autre, et aucun des
// deux registres n'était faux de son côté.
//
// Depuis, chaque mouvement de ce fichier a son pendant sur les gains :
//   • demande de retrait  → imputation des gains les plus anciens (Settled) ;
//   • refus, échec, rejet → les mêmes gains redeviennent payables (Released).
//
// Un recrédit de portefeuille SANS libération des gains laisserait le vendeur
// avec son argent au solde et des gains soldés qui n'entreraient plus dans
// aucun lot : il serait payé, puis payé encore, et rien ne se solderait.
// ════════════════════════════════════════════════════════════════════════════
// ============================================================================

/// <summary>
/// Demande de retrait d'un vendeur : retient les fonds (débit du solde principal)
/// et crée une demande en attente de validation admin. NE déclenche PAS de payout.
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

        // ═════════════════════════════════════════════════════════════════════
        // `GetSellerPayoutAsync`, ET SURTOUT PAS `GetSellerAsync().Payout`.
        //
        // Cette ligne lisait `seller?.Payout`. Le champ existe sur le record, mais
        // le proto gRPC ne le transporte pas — et wallet-service, hébergé par
        // payment-service, résout `ISellerModuleApi` sur le CLIENT gRPC. Le mappeur
        // y écrivait `Payout: null` en dur.
        //
        // Conséquence : AUCUN VENDEUR DE LA PLATEFORME NE POUVAIT SORTIR SON
        // ARGENT. Chaque demande était refusée par « Aucun compte de versement
        // Mobile Money configuré » — message que le vendeur lisait avec son numéro
        // MTN sous les yeux, et qui l'envoyait ressaisir un compte déjà là.
        //
        // Le RPC dédié transporte réellement le compte, et sans cache : payer un
        // numéro périmé de dix minutes, c'est envoyer l'argent à l'ancien numéro
        // d'un vendeur qui vient de corriger une faute de frappe.
        //
        // On valide AVANT de retenir les fonds : aucune écriture inutile sinon.
        // ═════════════════════════════════════════════════════════════════════
        var payout = await _sellers.GetSellerPayoutAsync(command.SellerId, cancellationToken);

        // « VENDEUR INCONNU » N'EST PAS « VENDEUR SANS COMPTE ». Le premier est
        // une erreur d'identifiant — le second, une étape d'onboarding à terminer.
        // Les servir sous le même message est ce qui rendait le défaut ci-dessus
        // indétectable pour l'utilisateur comme pour le support.
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

        // Débite le solde principal (échoue si montant invalide / solde insuffisant).
        var debit = wallet.Withdraw(command.Amount);
        if (debit.IsFailure)
        {
            return Result.Failure<WithdrawalView>(debit.Error);
        }

        var currency = wallet.Currency;

        // LA DESTINATION EST FIGÉE ICI, PAS RELUE À L'APPROBATION.
        //
        // C'est le compte que le vendeur vise aujourd'hui, et celui que l'admin
        // verra dans sa file. Sans cette capture, modifier le compte entre la
        // demande et la validation détournait le virement — voir Withdrawal.
        var withdrawal = Withdrawal.Create(
            command.SellerId, command.Amount, currency,
            account.Provider, account.AccountNumber, account.AccountName);
        await _withdrawals.AddAsync(withdrawal, cancellationToken);
        await _ledger.AddAsync(WalletTransaction.ForSeller(
            command.SellerId, WalletAccount.Available, WalletDirection.Debit, command.Amount, currency,
            "withdrawal_request", "withdrawal", withdrawal.Id.Value), cancellationToken);

        // ═════════════════════════════════════════════════════════════════════
        // SANS CETTE LIGNE, LE GAIN RETIRÉ ICI SERA RE-VERSÉ PAR LE PROCHAIN LOT.
        //
        // Le portefeuille est débité, le versement partira — et les gains qui le
        // financent restaient « Released », donc payables. `RunSettlement` les
        // reprenait tels quels et créait un second versement pour le même argent.
        //
        // L'imputation se fait DANS LE MÊME SaveChanges que le débit : séparées,
        // un échec entre les deux laisserait un solde débité et des gains encore
        // payables — exactement le trou qu'on ferme.
        //
        // Le reliquat (montant retiré non couvert par des gains entiers) est
        // volontairement toléré : voir l'encadré de SellerEarningImputation. Il est
        // rattrapé par le plafonnement du lot au solde réel du portefeuille.
        // ═════════════════════════════════════════════════════════════════════
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
/// Money. Succès → Completed ; échec → Failed + recrédit des fonds.
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
        //
        // Cette ligne lisait `seller?.Payout`, que le proto ne transporte pas — donc
        // `null` pour tout le monde. L'échec ne se contentait pas de refuser : il
        // partait dans `FailAndRefundAsync`. L'administrateur cliquait « approuver »
        // et la demande était DÉTRUITE, avec remboursement, sur un motif faux.
        // Toute demande déjà en base se serait éteinte de cette façon, une par une,
        // au premier geste d'administration.
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

        // ═════════════════════════════════════════════════════════════════════
        // ON PAIE LA DESTINATION FIGÉE À LA DEMANDE — ET ON REFUSE SI ELLE A BOUGÉ.
        //
        // Auparavant, ce handler relisait simplement le compte COURANT du vendeur.
        // Modifier ce compte entre la demande et la validation suffisait donc à
        // détourner le virement : l'admin approuvait un montant qu'il avait vu, vers
        // une destination qu'il n'avait pas vue.
        //
        // ON REFUSE PLUTÔT QUE DE CHOISIR.
        //
        // Payer l'ancien compte enverrait l'argent là où le vendeur ne l'attend
        // plus — il a peut-être corrigé un numéro mal saisi. Payer le nouveau,
        // c'est le trou qu'on ferme. Aucune des deux options n'est défendable sans
        // un humain : la demande est PÉRIMÉE, on la rejette et le vendeur en refait
        // une, qui portera la bonne destination et repassera devant l'admin.
        //
        // Les fonds sont recrédités par FailAndRefundAsync : le vendeur ne perd rien
        // d'autre qu'un aller-retour.
        // ═════════════════════════════════════════════════════════════════════
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
        // pas. Elles retombent sur le compte courant — le comportement d'origine,
        // avec sa faille — mais cela se voit dans le motif d'échec plutôt que de
        // passer inaperçu. Elles s'éteindront d'elles-mêmes une fois traitées.
        var msisdn = withdrawal.PayoutAccountNumber ?? account.AccountNumber;
        var provider = withdrawal.PayoutProvider ?? account.Provider;
        var beneficiaire = withdrawal.PayoutAccountName ?? account.AccountName;

        // LE NOM DE BOUTIQUE N'EST LU QUE SI LE BÉNÉFICIAIRE MANQUE.
        //
        // Il était lu à chaque versement, sur un `seller!` dont la non-nullité
        // tenait au fait que le contrôle précédent passait par `GetSellerAsync`.
        // Ce contrôle passe maintenant par le compte de reversement : le vendeur
        // n'est plus chargé pour rien, et le `!` — qui aurait explosé le jour où
        // les deux lectures auraient divergé — disparaît avec lui.
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
                // L'opérateur du vendeur détermine à lui seul le routage PSP (mode ET
                // pays). On ne passe plus de code pays « en dur » : c'était le bug qui
                // expédiait tous les numéros vers le Bénin.
                Provider: account.Provider,
                Reference: $"withdrawal:{withdrawal.Id}"),
            cancellationToken);

        switch (outcome.Status)
        {
            // Rejet DÉFINITIF du PSP : rien n'est parti → on peut recréditer sans risque.
            case PayoutOutcomeStatus.Failed:
                return await FailAndRefundAsync(withdrawal, wallet, outcome.Error ?? "Échec du versement.", cancellationToken);

            // Issue INDÉTERMINÉE (timeout, 5xx…) : le versement est peut-être parti.
            // On NE REMBOURSE PAS — sinon l'admin pourrait re-valider et déclencher un
            // SECOND versement. Le retrait passe « en cours » ; la réconciliation
            // interrogera FedaPay et tranchera (Completed ou Failed + remboursement).
            case PayoutOutcomeStatus.Unknown:
                withdrawal.MarkProcessing(outcome.ProviderReference, outcome.Error);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);

            // Accepté = créé + démarré chez FedaPay. Ce n'est QUE « started » : l'argent
            // n'est pas encore arrivé. On ne clôture donc PAS en Completed ici — c'est la
            // réconciliation, sur le statut « sent », qui le fera.
            default:
                withdrawal.MarkProcessing(outcome.ProviderReference);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return WalletMapper.ToView(withdrawal);
        }
    }

    // Échec du payout après retenue des fonds : marque Failed et recrédite le solde.
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
    /// Opérateurs que l'on sait RÉELLEMENT reverser via FedaPay (mode + pays connus).
    /// Doit rester aligné sur <c>FedaPayPayoutGateway.ResolveRoute</c>.
    ///
    /// « wave » et « fedapay » ont été RETIRÉS volontairement : « fedapay » n'est pas
    /// un opérateur, et « wave » est ambigu (FedaPay distingue wave_ci et wave_sn — sans
    /// pays sur le compte du vendeur, impossible de trancher). Les accepter revenait à
    /// router leurs numéros vers MTN Bénin. Mieux vaut refuser la demande de retrait tout
    /// de suite que de débiter le vendeur pour échouer — ou pire, mal payer — ensuite.
    ///
    /// Pour les supporter : ajouter un pays au compte de versement du vendeur, puis
    /// étendre la table de routage du gateway.
    /// </summary>
    public static bool IsMobileMoney(string provider)
        => provider.ToLowerInvariant() is "mtnmomo" or "moovmoney" or "celtis";
}
