using FluentValidation;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Domain.Batches;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Financial.Wallet.Domain.Wallets;
using Microsoft.Extensions.Logging;

namespace HBA.Financial.Wallet.Application.Batches;

/// <summary>
/// Types de référence des écritures de reversement au grand livre.
///
/// L'ALLER ET LE RETOUR NE PORTENT PAS LE MÊME TYPE, ET CE N'EST PAS UN DÉTAIL.
///
/// Le débit du lot et son recrédit à l'annulation partagent le MÊME identifiant —
/// le lot — parce qu'ils se lisent ensemble. Leur donner aussi le même TYPE les
/// rendrait indistinguables dans toute recherche par référence, et surtout
/// interdirait à jamais d'ajouter un index d'unicité sur (type, référence) : la
/// contrainte sauterait à la première annulation. C'est exactement le piège dans
/// lequel le crédit du livreur était tombé (voir `DriverShareReferenceType`).
/// </summary>
internal static class SettlementLedger
{
    public const string PayoutReferenceType = "settlement";
    public const string ReversalReferenceType = "settlement_reversal";
}

/// <summary>
/// Génère un lot de reversements pour la période : un payout net par vendeur.
/// <paramref name="SellerId"/> restreint le lot à UN vendeur ; null = tous.
/// </summary>
public sealed record RunSettlementCommand(DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid? SellerId = null) : ICommand<Guid>;

/// <summary>Marque un reversement comme versé (simule le transfert MoMo/banque).</summary>
public sealed record MarkPayoutPaidCommand(Guid BatchId, Guid PayoutId, string ProviderRef) : ICommand;

/// <summary>
/// Marque un reversement comme REFUSÉ par l'opérateur, et le compense : le
/// vendeur est recrédité, une contre-écriture est portée au grand livre, et SES
/// gains du lot redeviennent payables pour un lot ultérieur.
///
/// <paramref name="Reason"/> n'est PAS persisté — voir le gestionnaire.
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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE LOT DÉBITE DÉSORMAIS LE PORTEFEUILLE, COMME UN RETRAIT.
///
/// IL NE LE FAISAIT PAS, ET C'ÉTAIT LA MOITIÉ DU DOUBLE PAIEMENT.
///
/// Deux chemins mènent l'argent au vendeur, et aucun ne voyait l'autre : le
/// retrait à la demande débitait le portefeuille sans toucher aux gains ; ce lot
/// marquait les gains « soldés » sans toucher au portefeuille. Un gain déjà
/// encaissé par retrait — argent réellement parti chez FedaPay — restait
/// « Released », donc payable, et ce lot le repayait. Perte sèche.
///
/// Le portefeuille fait foi. Deux conséquences, et la seconde n'est pas
/// évidente :
///
///   1. Un gain déjà sorti du portefeuille ne peut plus être reversé : le retrait
///      le marque « Settled » à la demande, il n'apparaît donc plus ici.
///
///   2. Le montant versé à un vendeur est PLAFONNÉ à son solde disponible réel.
///      C'est ce plafond qui absorbe le reliquat d'imputation d'un retrait — la
///      part d'un gain que le retrait a consommée sans le solder entièrement
///      (voir `SellerEarningImputation`). Sans lui, le lot repaierait ce
///      reliquat, et le total dépasserait ce que le vendeur a gagné.
///
/// ET C'EST AUSSI CE QUI REND LA CONCURRENCE SÛRE.
///
/// `seller_wallets` porte un verrou optimiste (`xmin`, voir
/// SellerWalletConfiguration). En passant par le portefeuille, ce lot entre sous
/// ce verrou : deux exécutions simultanées, ou une exécution pendant qu'un
/// vendeur demande un retrait, se disputent la MÊME ligne, et la seconde échoue
/// en 409 au lieu de payer deux fois. Tant que le lot ne touchait pas au
/// portefeuille, aucun verrou ne pouvait les départager.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        // somme donnerait un total dénué de sens. On ne règle donc que les gains de
        // CETTE devise ; ceux d'une autre devise restent « Released » pour un prochain
        // lot (aucun effet en XOF pur aujourd'hui, mais le bug latent est fermé).
        var settleable = earnings.Where(e => e.Currency == currency).ToList();

        var batchResult = SettlementBatch.Create(command.PeriodStartUtc, command.PeriodEndUtc, currency);
        if (batchResult.IsFailure)
        {
            return Result.Failure<Guid>(batchResult.Error);
        }

        var batch = batchResult.Value;

        // ═════════════════════════════════════════════════════════════════════
        // LES PORTEFEUILLES SONT LUS EN UNE FOIS, PLUS UN PAR VENDEUR (§11).
        //
        // Cette boucle appelait `GetBySellerAsync` à chaque tour : cinq mille
        // vendeurs, cinq mille allers-terours — dans la commande la plus lourde de
        // la plateforme, celle qui paie tout le monde. Une seule lecture indexée
        // les rend tous.
        //
        // `groupes` EST MATÉRIALISÉ AVANT. `GroupBy` sur `IEnumerable` est
        // paresseux : le parcourir deux fois — une fois pour les clés, une fois
        // pour la boucle — le réévaluerait. Sur une liste en mémoire c'est
        // seulement du travail perdu ; le figer coûte moins cher que de se
        // demander si c'en est.
        // ═════════════════════════════════════════════════════════════════════
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
                //
                // Un gain payable sans portefeuille est une incohérence : le
                // portefeuille naît à la confirmation de la première commande, avant
                // le gain. En créer un ici, vide, permettrait au lot de « verser »
                // zéro franc et de solder les gains au passage — ils
                // disparaîtraient du circuit sans que personne ne soit payé.
                _logger.LogError(
                    "Reversement : le vendeur {SellerId} a {Count} gain(s) payable(s) mais AUCUN portefeuille. "
                    + "Ils restent payables et ne sont pas inclus dans le lot.",
                    sellerIdOfGroup, group.Count());

                continue;
            }

            if (!string.Equals(wallet.Currency, currency, StringComparison.Ordinal))
            {
                // Même raison que le lot mono-devise plus haut : on ne débite pas un
                // solde en XOF d'un montant exprimé dans une autre devise.
                _logger.LogError(
                    "Reversement : portefeuille du vendeur {SellerId} en {WalletCurrency}, lot en {BatchCurrency}. Vendeur ignoré.",
                    sellerIdOfGroup, wallet.Currency, currency);

                continue;
            }

            // ═════════════════════════════════════════════════════════════════
            // LE NET RESTANT, PAS LE NET D'ORIGINE.
            //
            // Cette somme portait sur `NetAmount` — le montant inscrit à la vente,
            // qui ne bouge jamais. Un gain dont la vente avait été REMBOURSÉE y
            // entrait donc en entier : la plateforme rendait l'argent au client,
            // reprenait le net au portefeuille du vendeur… puis le lui reversait
            // quand même par ce lot. Le remboursement était payé deux fois.
            //
            // `RemainingNetAmount` retranche ce qui a déjà été repris (voir
            // `SellerEarning.Reverse`). Un gain ENTIÈREMENT repris ne parvient même
            // plus jusqu'ici : son statut passe « Reversed », et
            // `ListReleasedInPeriodAsync` ne rend que les « Released ». Ce qui reste
            // à traiter ici, ce sont les reprises PARTIELLES — un article sur trois
            // rendu — pour lesquelles le gain demeure payable, mais amputé.
            // ═════════════════════════════════════════════════════════════════
            var net = group.Sum(e => e.RemainingNetAmount);

            // ═════════════════════════════════════════════════════════════════
            // LE SOLDE PLAFONNE LE VERSEMENT — C'EST TOUT L'ENJEU.
            //
            // Le lot versait la somme des gains payables, sans regarder le solde.
            // Un vendeur ayant déjà retiré une partie de cet argent était donc
            // payé deux fois pour la même vente.
            //
            // Le solde peut aussi être NÉGATIF : `DebitForRefund` le descend sous
            // zéro quand un retour survient après que le vendeur a tout retiré
            // (dette réelle, volontairement visible). Dans ce cas on ne verse
            // rien, et les gains restent payables — la dette se résorbera sur les
            // ventes suivantes, puis le lot suivant paiera le reste.
            // ═════════════════════════════════════════════════════════════════
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
                // au-dessus. On abandonne le vendeur plutôt que le lot entier — et
                // on le dit, parce que si cela arrive, le raisonnement ci-dessus est
                // faux quelque part.
                _logger.LogError(
                    "Reversement : débit de {Payable} {Currency} REFUSÉ pour le vendeur {SellerId} ({Code}). Vendeur ignoré.",
                    payable, currency, sellerIdOfGroup, debit.Error.Code);

                continue;
            }

            // LE BRUT ET LA COMMISSION DÉCRIVENT LES VENTES, LE NET CE QUI PART.
            //
            // Quand le versement est plafonné, `net` n'est plus égal à
            // `brut − commission` : l'écart est ce qu'un retrait avait déjà sorti.
            // Aligner artificiellement le brut sur le net effacerait cette trace et
            // rendrait le relevé du vendeur incompréhensible.
            //
            // MAIS LES REPRISES, ELLES, SONT DÉDUITES À LA SOURCE.
            //
            // Le plafond ci-dessus et la reprise d'un remboursement ne sont pas la
            // même chose et ne se traitent pas pareil. Le plafond dit « une partie
            // de cet argent est déjà sortie par un retrait » : la vente a bien eu
            // lieu, son brut reste vrai. Une reprise dit « cette vente n'a pas eu
            // lieu » : afficher son brut au payout ferait figurer au relevé du
            // vendeur une vente qui lui a été reprise, et le lot annoncerait un
            // chiffre d'affaires qui n'existe pas.
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
            // qu'il devait pour eux. Les laisser payables les ferait revenir dans
            // un lot qui, lui, ne trouverait plus de solde à débiter.
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
            // débité. Un lot vide n'a rien à suivre : on n'en crée pas.
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

        // ═════════════════════════════════════════════════════════════════════
        // ON SORT AVANT `Cancel()` SI LE LOT EST DÉJÀ ANNULÉ.
        //
        // `SettlementBatch.Cancel()` est IDEMPOTENT : il renvoie un succès sur un
        // lot déjà annulé. C'était sans conséquence tant que l'annulation ne
        // faisait que détacher des gains. Depuis qu'elle RECRÉDITE le
        // portefeuille, deux appels — un double-clic, un rejeu — créditeraient
        // deux fois le vendeur du montant du lot. De l'argent créé à partir de
        // rien.
        //
        // Le statut du lot est donc lu AVANT, et sert lui-même de verrou : sa
        // ligne est écrite dans la même transaction que les crédits.
        // ═════════════════════════════════════════════════════════════════════
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

        // ═════════════════════════════════════════════════════════════════════
        // LE RECRÉDIT DU PORTEFEUILLE MANQUAIT, ET IL N'ÉTAIT PAS UN OUBLI :
        // LE LOT NE DÉBITAIT RIEN.
        //
        // Maintenant que la création du lot débite le solde principal de chaque
        // vendeur, son annulation doit le rendre. Sans cela, annuler un lot
        // détacherait les gains — qui redeviendraient payables — pendant que
        // l'argent resterait sorti du portefeuille : le lot suivant les reprendrait
        // et se retrouverait plafonné à zéro. Le vendeur ne serait jamais payé, et
        // rien n'indiquerait pourquoi.
        //
        // `Cancel()` a déjà refusé le cas d'un versement parti : tous les payouts
        // ici sont « Scheduled » ou « Failed », donc aucun argent réel n'a quitté
        // la plateforme.
        // ═════════════════════════════════════════════════════════════════════
        // MÊME CORRECTION QUE LA CRÉATION DU LOT, ET L'AUDIT N'AVAIT VU QUE
        // L'AUTRE : annuler un lot relisait aussi un portefeuille par versement.
        // C'est le chemin qui REND l'argent — il n'a pas moins besoin d'être tenu.
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

        // Les gains du lot redeviennent payables : sans ça, ils resteraient « soldés »
        // pour un lot annulé — donc jamais reversés, et invisibles des lots suivants.
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
        //
        // Les fonds ont quitté le solde principal à la CRÉATION du lot, comme une
        // demande de retrait retient les fonds avant la validation admin. Débiter
        // une seconde fois à la confirmation du versement paierait deux fois.
        //
        // La symétrie est désormais complète : un versement REFUSÉ par l'opérateur
        // se compense par `MarkPayoutFailedCommand`, plus bas dans ce fichier. Et
        // marquer « payé » FERME cette porte — voir `SettlementBatch.MarkPayoutFailed`,
        // qui refuse la transition depuis « Paid » : l'argent est parti, le
        // recréditer le ferait sortir deux fois.
        var result = batch.MarkPayoutPaid(command.PayoutId, command.ProviderRef);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}


/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA COMPENSATION D'UN VIREMENT REFUSÉ — LE RETOUR QUI N'EXISTAIT PAS.
///
/// UN VENDEUR POUVAIT ÊTRE DÉBITÉ ET JAMAIS PAYÉ, SANS QUE RIEN NE LE DISE.
///
/// La création du lot débite IMMÉDIATEMENT le portefeuille du vendeur, porte une
/// écriture `settlement_payout` au grand livre et solde ses gains. L'aller était
/// donc complet dès la création ; le retour, lui, n'existait que pour le lot
/// ENTIER (`CancelSettlementBatchCommandHandler`). Si l'opérateur refusait UN
/// virement — numéro Mobile Money erroné, compte fermé, plafond dépassé — il ne se
/// passait rien : `MarkPayoutFailed` n'avait aucun appelant, aucune commande,
/// aucune route. Les fonds restaient sortis du portefeuille, les gains restaient
/// soldés, et `PayoutStatus.Failed` était un état que rien ne posait jamais.
///
/// Ce gestionnaire est le SYMÉTRIQUE EXACT de l'annulation de lot, à la portée
/// près : un seul versement au lieu de tous. Les trois gestes sont les mêmes et
/// dans le même ordre — recréditer le solde principal, porter la contre-écriture,
/// dé-solder les gains — parce que ce sont les trois effets que la création du lot
/// avait produits.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ON LIT LE STATUT DU VERSEMENT AVANT D'ÉCRIRE, ET C'EST LE VERROU.
///
/// Même raisonnement, et mêmes conséquences, que sur `Cancel()` :
/// `MarkPayoutFailed` est IDEMPOTENT sur un versement déjà échoué (elle rend un
/// succès sans rien changer). Enchaîner sur le recrédit sans lire le statut
/// d'abord créditerait le vendeur DEUX fois au second appel — un double-clic
/// suffit. La ligne du versement est écrite dans la même transaction que le
/// crédit : son statut sert donc lui-même de verrou.
///
/// UN VERSEMENT DÉJÀ « PAID » EST REFUSÉ, PAS COMPENSÉ.
///
/// Le domaine tranche (voir `SettlementBatch.MarkPayoutFailed`) : l'argent est
/// parti chez l'opérateur, le recréditer le ferait sortir une seconde fois. Il n'y
/// a aucun rattrapage automatique pour un virement parti puis rejeté — c'est un
/// impayé, il se traite à la main.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// CE QUE CE GESTIONNAIRE NE FAIT PAS : IL NE CONSERVE PAS LE MOTIF.
///
/// `Payout` n'a pas de colonne pour le motif d'échec, et en ajouter une exigerait
/// une migration sur `payouts` qui n'est pas dans ce lot. Le motif ne vit donc que
/// dans le JOURNAL. Concrètement : l'administration ne peut pas afficher « pourquoi
/// ce virement a échoué », et deux échecs successifs sur le même vendeur ne se
/// distinguent qu'en relisant les logs. C'est une lacune connue, pas un oubli.
/// </summary>
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
        // marqué échoué. Le domaine rendrait un succès, et le recrédit partirait une
        // seconde fois.
        if (payout.Status == PayoutStatus.Failed)
        {
            return Result.Success();
        }

        // Refuse la transition depuis « Paid ». Le motif du refus vient du domaine,
        // pour que la règle vive à un seul endroit.
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
            // portefeuille pour y verser une compensation. Un versement sans
            // portefeuille est une incohérence qui précède ce geste ; en fabriquer un
            // ici masquerait la cause et donnerait un solde né d'un échec.
            //
            // Le versement reste marqué « Failed » et les gains sont tout de même
            // dé-soldés : sans cela, ils resteraient rattachés à un versement mort et
            // ne reviendraient dans AUCUN lot. Le vendeur redevient donc payable
            // pendant que sa compensation est portée à la main.
            _logger.LogError(
                "Versement {PayoutId} du lot {BatchId} refusé ({Motif}) : portefeuille INTROUVABLE pour le "
                + "vendeur {SellerId}. {Montant} {Currency} ne sont PAS recrédités — écriture manuelle requise.",
                command.PayoutId, command.BatchId, motif, payout.SellerId, payout.NetAmount, payout.Currency);
        }
        else
        {
            wallet.CreditAvailable(payout.NetAmount);

            // MÊME TYPE DE RÉFÉRENCE QUE L'ANNULATION, MOTIF DIFFÉRENT.
            //
            // Le type (`settlement_reversal`) et l'identifiant (le LOT) alignent cette
            // écriture sur celle de l'annulation : les deux se lisent contre le débit
            // `settlement` du même lot, et rester distinct du type ALLER est ce qui
            // garde l'aller et le retour discernables (voir `SettlementLedger`).
            //
            // C'est le motif — `settlement_payout_failed` — qui dit lequel des deux
            // retours a joué. Un lot peut porter PLUSIEURS contre-écritures (un
            // versement refusé par vendeur) : ce couple (type, référence) n'est donc
            // pas unique, exactement comme pour l'annulation, et aucun index
            // d'unicité ne peut être posé dessus.
            await _ledger.AddAsync(WalletTransaction.ForSeller(
                payout.SellerId, WalletAccount.Available, WalletDirection.Credit, payout.NetAmount, payout.Currency,
                "settlement_payout_failed", SettlementLedger.ReversalReferenceType, command.BatchId), cancellationToken);
        }

        // ON NE DÉ-SOLDE QUE LES GAINS DE CE VENDEUR.
        //
        // `ListByBatchAsync` rend les gains de TOUT le lot : les rendre tous payables
        // détacherait ceux des vendeurs dont le virement est parti sans encombre, et
        // le lot suivant les repaierait. Le filtre sur le vendeur est ce qui distingue
        // cette compensation de celle du lot entier.
        //
        // `Unsettle()` est idempotente et ne touche qu'un gain « Settled » : un gain
        // entre-temps REPRIS par un remboursement (statut « Reversed ») n'est pas
        // ressuscité — il ne doit plus jamais être payable.
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
