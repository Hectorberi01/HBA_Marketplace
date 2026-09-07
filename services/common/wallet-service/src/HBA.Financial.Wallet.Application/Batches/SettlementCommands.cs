using FluentValidation;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;
using Microsoft.Extensions.Logging;

namespace HBA.Financial.Wallet.Application.Batches;

/// <summary>Types de référence des écritures de reversement au grand livre.</summary>
internal static class SettlementLedger
{
    public const string PayoutReferenceType = "settlement";
    public const string ReversalReferenceType = "settlement_reversal";
}

/// <summary>Génère un lot de reversements pour la période : un payout net par vendeur.</summary>
public sealed record RunSettlementCommand(DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid? SellerId = null) : ICommand<Guid>;

/// <summary>Marque un reversement comme versé (simule le transfert MoMo/banque).</summary>
public sealed record MarkPayoutPaidCommand(Guid BatchId, Guid PayoutId, string ProviderRef) : ICommand;

/// <summary>
/// Marque un reversement comme REFUSÉ par l'opérateur, et le compense : le vendeur
/// est recrédité, une contre-écriture est portée au grand livre, et SES gains du
/// lot redeviennent payables pour un lot ultérieur.
/// </summary>
public sealed record MarkPayoutFailedCommand(Guid BatchId, Guid PayoutId, string Reason) : ICommand;

/// <summary>
/// Annule un lot encore en attente : le lot passe « Cancelled » et ses gains
/// redeviennent payables (ils pourront entrer dans un prochain lot).
/// </summary>
public sealed record CancelSettlementBatchCommand(Guid BatchId) : ICommand;

public sealed class RunSettlementCommandValidator : AbstractValidator<RunSettlementCommand>
{
    public RunSettlementCommandValidator()
        => RuleFor(c => c.PeriodEndUtc).GreaterThan(c => c.PeriodStartUtc).WithMessage("Période invalide.");
}

/// <summary>LE LOT DÉBITE DÉSORMAIS LE PORTEFEUILLE, COMME UN RETRAIT.</summary>
internal sealed class RunSettlementCommandHandler : ICommandHandler<RunSettlementCommand, Guid>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISellerWalletRepository _walletRepository;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<RunSettlementCommandHandler> _logger;

    public RunSettlementCommandHandler(
        ISellerEarningRepository earningRepository,
        ISettlementBatchRepository batchRepository,
        ISellerWalletRepository walletRepository,
        IWalletTransactionRepository ledger,
        IWalletUnitOfWork unitOfWork,
        ILogger<RunSettlementCommandHandler> logger)
    {
        _earningRepository = earningRepository;
        _batchRepository = batchRepository;
        _walletRepository = walletRepository;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(RunSettlementCommand command, CancellationToken cancellationToken)
    {
        // On ne reverse que les gains LIBÉRÉS (escrow levé à la livraison), pas les
        // gains encore en escrow (simplement confirmés).
        var released = await _earningRepository.ListReleasedInPeriodAsync(command.PeriodStartUtc, command.PeriodEndUtc, cancellationToken);

        // Ciblage d'UN vendeur : on filtre après lecture plutôt que d'ajouter une
        // surcharge au repository — la période borne déjà fortement le volume.
        var earnings = command.SellerId is { } sellerId
            ? released.Where(e => e.SellerId == sellerId).ToList()
            : released;

        if (earnings.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict("settlement.nothing_to_settle", "Aucun gain payable (livré) à reverser sur cette période."));
        }

        var currency = earnings[0].Currency;

        // Un lot est STRICTEMENT mono-devise : le batch porte une seule devise et
        // additionne des montants — mélanger XOF et une autre devise dans une même
        // somme donnerait un total dénué de sens.
        var settleable = earnings.Where(e => e.Currency == currency).ToList();

        var batchResult = SettlementBatch.Create(command.PeriodStartUtc, command.PeriodEndUtc, currency);
        if (batchResult.IsFailure)
        {
            return Result.Failure<Guid>(batchResult.Error);
        }

        var batch = batchResult.Value;

        // LES PORTEFEUILLES SONT LUS EN UNE FOIS, PLUS UN PAR VENDEUR (§11).
        var groupes = settleable.GroupBy(e => e.SellerId).ToList();

        var portefeuilles = await _walletRepository.ListBySellersAsync(
            groupes.Select(g => g.Key).ToList(), cancellationToken);

        foreach (var group in groupes)
        {
            var sellerIdOfGroup = group.Key;
            portefeuilles.TryGetValue(sellerIdOfGroup, out var wallet);

            if (wallet is null)
            {
                // ON NE VERSE PAS SANS PORTEFEUILLE, ON NE LE CRÉE PAS NON PLUS.
                _logger.LogError(
                    "Reversement : le vendeur {SellerId} a {Count} gain(s) payable(s) mais AUCUN portefeuille. "
                    + "Ils restent payables et ne sont pas inclus dans le lot.",
                    sellerIdOfGroup, group.Count());

                continue;
            }

            if (!string.Equals(wallet.Currency, currency, StringComparison.Ordinal))
            {
                // Même raison que le lot mono-devise plus haut : on ne débite pas
                // un solde en XOF d'un montant exprimé dans une autre devise.
                _logger.LogError(
                    "Reversement : portefeuille du vendeur {SellerId} en {WalletCurrency}, lot en {BatchCurrency}. Vendeur ignoré.",
                    sellerIdOfGroup, wallet.Currency, currency);

                continue;
            }

            // LE NET RESTANT, PAS LE NET D'ORIGINE.
            var net = group.Sum(e => e.RemainingNetAmount);

            // LE SOLDE PLAFONNE LE VERSEMENT — C'EST TOUT L'ENJEU.
            var payable = Math.Min(net, wallet.AvailableBalance);

            if (payable <= 0m)
            {
                _logger.LogWarning(
                    "Reversement : vendeur {SellerId}, {Net} {Currency} de gains payables mais solde disponible de "
                    + "{Solde}. Rien n'est versé, les gains restent payables.",
                    sellerIdOfGroup, net, currency, wallet.AvailableBalance);

                continue;
            }

            var debit = wallet.Withdraw(payable);
            if (debit.IsFailure)
            {
                // Ne devrait pas survenir : `payable` est borné par le solde juste
                // au-dessus.
                _logger.LogError(
                    "Reversement : débit de {Payable} {Currency} REFUSÉ pour le vendeur {SellerId} ({Code}). Vendeur ignoré.",
                    payable, currency, sellerIdOfGroup, debit.Error.Code);

                continue;
            }

            // LE BRUT ET LA COMMISSION DÉCRIVENT LES VENTES, LE NET CE QUI PART.
            batch.AddPayout(
                sellerIdOfGroup,
                group.Sum(e => e.RemainingGrossAmount),
                group.Sum(e => e.RemainingCommissionAmount),
                payable);

            await _ledger.AddAsync(WalletTransaction.ForSeller(
                sellerIdOfGroup, WalletAccount.Available, WalletDirection.Debit, payable, currency,
                "settlement_payout", SettlementLedger.PayoutReferenceType, batch.Id.Value), cancellationToken);

            // Les gains du vendeur sont soldés en BLOC, y compris celui qui n'est
            // que partiellement couvert : le portefeuille a déjà rendu tout ce
            // qu'il devait pour eux.
            foreach (var earning in group)
            {
                earning.MarkSettled(batch.Id.Value);
            }

            if (payable < net)
            {
                _logger.LogInformation(
                    "Reversement : vendeur {SellerId} plafonné à {Payable} {Currency} pour {Net} de gains — "
                    + "l'écart avait déjà été retiré.",
                    sellerIdOfGroup, payable, currency, net);
            }
        }

        if (batch.Payouts.Count == 0)
        {
            // Des gains payables existaient, mais aucun portefeuille n'a pu être
            // débité.
            return Result.Failure<Guid>(Error.Conflict(
                "settlement.nothing_payable",
                "Aucun vendeur n'a de solde disponible à reverser sur cette période."));
        }

        await _batchRepository.AddAsync(batch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return batch.Id.Value;
    }
}

internal sealed class CancelSettlementBatchCommandHandler : ICommandHandler<CancelSettlementBatchCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISellerWalletRepository _walletRepository;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<CancelSettlementBatchCommandHandler> _logger;

    public CancelSettlementBatchCommandHandler(
        ISettlementBatchRepository batchRepository,
        ISellerEarningRepository earningRepository,
        ISellerWalletRepository walletRepository,
        IWalletTransactionRepository ledger,
        IWalletUnitOfWork unitOfWork,
        ILogger<CancelSettlementBatchCommandHandler> logger)
    {
        _batchRepository = batchRepository;
        _earningRepository = earningRepository;
        _walletRepository = walletRepository;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(CancelSettlementBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        // ON SORT AVANT `Cancel()` SI LE LOT EST DÉJÀ ANNULÉ.
        if (batch.Status == SettlementStatus.Cancelled)
        {
            return Result.Success();
        }

        // Le domaine refuse l'annulation si un versement est déjà parti.
        var result = batch.Cancel();
        if (result.IsFailure)
        {
            return result;
        }

        // LE RECRÉDIT DU PORTEFEUILLE MANQUAIT, ET IL N'ÉTAIT PAS UN OUBLI : LE LOT
        // NE DÉBITAIT RIEN.
        var portefeuilles = await _walletRepository.ListBySellersAsync(
            batch.Payouts.Select(p => p.SellerId).ToList(), cancellationToken);

        foreach (var payout in batch.Payouts)
        {
            if (!portefeuilles.TryGetValue(payout.SellerId, out var wallet))
            {
                _logger.LogError(
                    "Annulation du lot {BatchId} : portefeuille INTROUVABLE pour le vendeur {SellerId}. "
                    + "{Montant} {Currency} ne sont pas recrédités.",
                    command.BatchId, payout.SellerId, payout.NetAmount, payout.Currency);

                continue;
            }

            wallet.CreditAvailable(payout.NetAmount);
            await _ledger.AddAsync(WalletTransaction.ForSeller(
                payout.SellerId, WalletAccount.Available, WalletDirection.Credit, payout.NetAmount, payout.Currency,
                "settlement_cancel", SettlementLedger.ReversalReferenceType, command.BatchId), cancellationToken);
        }

        // Les gains du lot redeviennent payables : sans ça, ils resteraient «
        // soldés » pour un lot annulé — donc jamais reversés, et invisibles des
        // lots suivants.
        var earnings = await _earningRepository.ListByBatchAsync(command.BatchId, cancellationToken);
        foreach (var earning in earnings)
        {
            earning.Unsettle();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class MarkPayoutPaidCommandHandler : ICommandHandler<MarkPayoutPaidCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly IWalletUnitOfWork _unitOfWork;

    public MarkPayoutPaidCommandHandler(ISettlementBatchRepository batchRepository, IWalletUnitOfWork unitOfWork)
    {
        _batchRepository = batchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkPayoutPaidCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        // AUCUN MOUVEMENT DE PORTEFEUILLE ICI, ET C'EST VOULU.
        var result = batch.MarkPayoutPaid(command.PayoutId, command.ProviderRef);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}


/// <summary>LA COMPENSATION D'UN VIREMENT REFUSÉ — LE RETOUR QUI N'EXISTAIT PAS.</summary>
internal sealed class MarkPayoutFailedCommandHandler : ICommandHandler<MarkPayoutFailedCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISellerWalletRepository _walletRepository;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<MarkPayoutFailedCommandHandler> _logger;

    public MarkPayoutFailedCommandHandler(
        ISettlementBatchRepository batchRepository,
        ISellerEarningRepository earningRepository,
        ISellerWalletRepository walletRepository,
        IWalletTransactionRepository ledger,
        IWalletUnitOfWork unitOfWork,
        ILogger<MarkPayoutFailedCommandHandler> logger)
    {
        _batchRepository = batchRepository;
        _earningRepository = earningRepository;
        _walletRepository = walletRepository;
        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkPayoutFailedCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        var payout = batch.Payouts.FirstOrDefault(p => p.Id == command.PayoutId);
        if (payout is null)
        {
            return Result.Failure(Error.NotFound("settlement.payout.not_found", "Reversement introuvable dans ce lot."));
        }

        // Voir l'encadré : on SORT AVANT toute écriture si le versement est déjà
        // marqué échoué.
        if (payout.Status == PayoutStatus.Failed)
        {
            return Result.Success();
        }

        // Refuse la transition depuis « Paid ».
        var resultat = batch.MarkPayoutFailed(command.PayoutId);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        var motif = string.IsNullOrWhiteSpace(command.Reason)
            ? "Virement refusé par l'opérateur."
            : command.Reason.Trim();

        var wallet = await _walletRepository.GetBySellerAsync(payout.SellerId, cancellationToken);
        if (wallet is null)
        {
            // Même arbitrage que sur l'annulation du lot : on ne CRÉE pas de
            // portefeuille pour y verser une compensation.
            _logger.LogError(
                "Versement {PayoutId} du lot {BatchId} refusé ({Motif}) : portefeuille INTROUVABLE pour le "
                + "vendeur {SellerId}. {Montant} {Currency} ne sont PAS recrédités — écriture manuelle requise.",
                command.PayoutId, command.BatchId, motif, payout.SellerId, payout.NetAmount, payout.Currency);
        }
        else
        {
            wallet.CreditAvailable(payout.NetAmount);

            // MÊME TYPE DE RÉFÉRENCE QUE L'ANNULATION, MOTIF DIFFÉRENT.
            await _ledger.AddAsync(WalletTransaction.ForSeller(
                payout.SellerId, WalletAccount.Available, WalletDirection.Credit, payout.NetAmount, payout.Currency,
                "settlement_payout_failed", SettlementLedger.ReversalReferenceType, command.BatchId), cancellationToken);
        }

        // ON NE DÉ-SOLDE QUE LES GAINS DE CE VENDEUR.
        var earnings = await _earningRepository.ListByBatchAsync(command.BatchId, cancellationToken);
        var duVendeur = earnings.Where(e => e.SellerId == payout.SellerId).ToList();
        foreach (var earning in duVendeur)
        {
            earning.Unsettle();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Versement {PayoutId} du lot {BatchId} REFUSÉ ({Motif}) : {Montant} {Currency} recrédités au vendeur "
            + "{SellerId}, {Count} gain(s) redevenus payables.",
            command.PayoutId, command.BatchId, motif, payout.NetAmount, payout.Currency, payout.SellerId,
            duVendeur.Count);

        return Result.Success();
    }
}
