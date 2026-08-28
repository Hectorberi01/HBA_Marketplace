using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE COMMANDE TOMBE APRÈS AVOIR ÉTÉ COMPTABILISÉE — ON REPREND TOUT.
///
/// CE HANDLER MANQUAIT, ET LE TROU EST PROPRE À LA RESTAURATION.
///
/// Pour la marchandise, la chronologie protégeait : une commande n'était
/// comptabilisée qu'à la confirmation, et n'était plus annulable ensuite. Le seul
/// retour en arrière passait par un RETOUR, que
/// `ReverseEarningsOnReturnRefundedHandler` couvre.
///
/// La restauration inverse cette chronologie. Le §24 place la décision du
/// restaurant APRÈS le paiement : la commande est confirmée — donc comptabilisée,
/// le solde « à venir » du restaurateur crédité, la commission et les frais
/// encaissés par la plateforme — ET ALORS la cuisine peut refuser. Ce refus
/// rembourse le client par `RefundPaymentCommand`, qui n'est PAS un retour et ne
/// publie donc rien que l'ancien handler écoute.
///
/// Sans ce fichier, chaque refus de restaurant laissait :
///   • un gain figé en « Accrued » que rien ne libère ni n'annule ;
///   • un solde à venir gonflé au nom du restaurateur, pour un repas jamais servi ;
///   • une commission et des frais de paiement encaissés sur un chiffre d'affaires
///     qui n'a pas eu lieu.
///
/// Rien de tout cela ne lève d'erreur. Cela se découvre au rapprochement
/// comptable, des semaines plus tard, sur des sommes déjà réclamées.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ReverseEarningsOnOrderCancelledHandler
    : IIntegrationEventHandler<OrderCancelledIntegrationEvent>
{
    private readonly ISellerEarningRepository _earnings;
    private readonly WalletMutations _wallets;
    private readonly IOrderingModuleApi _ordering;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly ILogger<ReverseEarningsOnOrderCancelledHandler> _logger;

    public ReverseEarningsOnOrderCancelledHandler(
        ISellerEarningRepository earnings,
        WalletMutations wallets,
        IOrderingModuleApi ordering,
        IWalletUnitOfWork unitOfWork,
        ILogger<ReverseEarningsOnOrderCancelledHandler> logger)
    {
        _earnings = earnings;
        _wallets = wallets;
        _ordering = ordering;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // AUCUNE GARDE SUR LA NATURE, ET C'EST DÉLIBÉRÉ.
        //
        // `OrderCancelledIntegrationEvent` ne porte pas `Kind`. Plutôt que de
        // l'enrichir pour un seul consommateur, on interroge le grand livre : une
        // commande de marchandise annulée avant confirmation n'y a AUCUNE écriture,
        // et le handler s'arrête à la ligne suivante. Une lecture indexée par
        // commande, contre une distinction que trois autres modules n'utiliseraient
        // pas.
        var gains = await _earnings.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);

        if (gains.Count == 0)
        {
            return;
        }

        // ═════════════════════════════════════════════════════════════════════
        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
        //
        // L'outbox livre AT-LEAST-ONCE, et le dispatcher rejoue TOUS les handlers
        // d'un message dès que l'un d'eux échoue — y compris ceux qui avaient
        // réussi. Sans ce garde, un rejeu débiterait une seconde fois le
        // restaurateur et restituerait la commission deux fois.
        //
        // C'est exactement l'oubli qui avait été corrigé sur la contre-passation
        // des retours ; le refaire ici serait le reproduire.
        //
        // La référence est la COMMANDE : une commande ne s'annule qu'une fois.
        // ═════════════════════════════════════════════════════════════════════
        if (await _wallets.RefundAlreadyReversedAsync(integrationEvent.OrderId, cancellationToken))
        {
            _logger.LogInformation(
                "Commande {OrderId} : contre-passation DÉJÀ effectuée — rejeu ignoré.",
                integrationEvent.OrderId);

            return;
        }

        // ON REPREND LES MONTANTS RÉELLEMENT ÉCRITS, PAS UN CALCUL REFAIT.
        //
        // L'annulation est TOTALE, et le grand livre porte déjà les trois montants
        // exacts, arrondis au moment de l'écriture. Les recalculer avec les taux
        // d'aujourd'hui produirait un écart au franc près dès qu'un taux aurait
        // changé entre-temps — un écart qui ne se voit qu'en soldant les comptes.
        //
        // C'est la même règle que sur les retours : un montant appliqué se relit, il
        // ne se recalcule pas. `ReverseEarningsOnReturnRefundedHandler` recalculait,
        // lui, à partir des taux courants et de la formule MARCHANDISE — il rendait
        // donc au restaurateur un net calculé au taux marchandise alors qu'on lui
        // avait prélevé le taux restauration. Il relit désormais, et n'applique plus
        // qu'un prorata lorsque le remboursement est partiel.
        // ═════════════════════════════════════════════════════════════════════
        // LE GAIN TRANCHE, LES SOLDES SUIVENT — CE QUI MANQUAIT ICI (audit 2.6bis,
        // constat 2.3).
        //
        // CE QUI ÉTAIT CASSÉ. Cette boucle débitait les trois soldes et ne touchait
        // JAMAIS au gain. Le `SellerEarning` restait au statut `Released` avec son
        // `NetAmount` intact, donc `ListReleasedInPeriodAsync` le ramassait, et le
        // lot de reversement suivant le PAYAIT au vendeur — pour une commande
        // annulée dont le solde avait déjà été repris.
        //
        // De l'argent versé deux fois, découvert au rapprochement bancaire, sur des
        // fonds déjà partis. Et le journal de fin de méthode annonçait
        // « N gain(s) contre-passé(s) » : la ligne qui aurait pu alerter affirmait
        // le contraire de ce qui se passait.
        //
        // C'EST EXACTEMENT LE DÉFAUT DÉJÀ CORRIGÉ SUR LES RETOURS. L'encadré de
        // `SellerEarning.ReversedGrossAmount` le disait mot pour mot :
        // « L'annulation de commande ne les alimente PAS non plus : elle débite le
        // portefeuille sans toucher au gain, exactement comme le faisait le retour
        // avant ce travail. C'est le même défaut, sur un autre chemin, et il reste
        // ouvert. » Il ne l'est plus.
        //
        // L'ORDRE EST CELUI DU HANDLER DE RETOUR, ET IL N'EST PAS ARBITRAIRE.
        // `Reverse` BORNE la reprise à ce qui reste du gain et rend ce qu'elle a
        // réellement pu inscrire. Débiter d'abord puis inscrire le raboté ferait
        // diverger le grand livre du gain. Les trois débits portent donc les
        // montants RENDUS par le domaine, pas ceux qu'on venait de lire.
        //
        // POURQUOI UNE REPRISE TOTALE ICI, ET PARTIELLE LÀ-BAS. Une annulation
        // porte sur la commande entière : on rend les quatre montants inscrits.
        // Un retour peut ne porter que sur un article, d'où le prorata côté retour.
        // ═════════════════════════════════════════════════════════════════════
        var refuses = 0;

        foreach (var gain in gains)
        {
            var reprise = gain.Reverse(
                gain.RemainingGrossAmount,
                gain.RemainingCommissionAmount,
                gain.RemainingProviderFeeAmount,
                gain.RemainingNetAmount);

            if (reprise.IsFailure)
            {
                // ON SAUTE LE GAIN, ON N'ÉCRIT RIEN, ET ON NE FAIT PAS ÉCHOUER LE
                // MESSAGE — même raisonnement que sur les retours.
                //
                // Le seul refus possible est « déjà entièrement repris » : un retour
                // antérieur avait déjà rendu la totalité de cette vente. Débiter
                // malgré tout reprendrait au vendeur de l'argent qu'il n'a pas
                // touché. Relancer ne changerait rien : le gain ne redeviendra
                // jamais reprenable.
                refuses++;

                _logger.LogWarning(
                    "Commande {OrderId} annulée : le gain {EarningId} refuse la reprise ({Code}) — "
                    + "{Net} {Currency} NE sont pas repris au vendeur {SellerId}.",
                    integrationEvent.OrderId, gain.Id.Value, reprise.Error.Code,
                    gain.RemainingNetAmount, gain.Currency, gain.SellerId);

                continue;
            }

            var applique = reprise.Value;

            await _wallets.DebitSellerForRefundAsync(
                gain.SellerId, applique.NetAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);

            await _wallets.DebitPlatformCommissionAsync(
                applique.CommissionAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);

            await _wallets.DebitPlatformProviderFeeAsync(
                applique.ProviderFeeAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LES FRAIS DE LIVRAISON AUSSI, ET C'EST LE POINT LE PLUS OUBLIÉ.
        //
        // `RefundPaymentCommand` rembourse le paiement INTÉGRAL — frais de
        // livraison compris, puisqu'ils font partie du total encaissé. Sans cette
        // reprise, la plateforme rendait 12 000 francs au client et gardait 2 000
        // au compte « livraison », pour une course qui n'a même pas été créée.
        //
        // Le montant vient de la COMMANDE et non d'un calcul : c'est exactement ce
        // qui avait été crédité.
        // ═════════════════════════════════════════════════════════════════════
        var commande = await _ordering.GetOrderAsync(integrationEvent.OrderId, cancellationToken);

        if (commande is { ShippingFee: > 0m })
        {
            await _wallets.DebitPlatformShippingAsync(
                commande.ShippingFee, commande.Currency,
                reason: "order_cancelled",
                referenceType: "order",
                referenceId: integrationEvent.OrderId,
                ct: cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LE COMPTE EST CELUI DES GAINS RÉELLEMENT REPRIS, PAS CELUI DES GAINS LUS.
        //
        // Cette ligne annonçait `gains.Count` « contre-passé(s) » alors qu'aucun ne
        // l'était. Une ligne de journal qui affirme un effet qui n'a pas eu lieu est
        // pire que pas de ligne du tout : elle ferme la question.
        _logger.LogInformation(
            "Commande {OrderId} annulée ({Reason}) : {Repris} gain(s) contre-passé(s) sur {Total} "
            + "({Refuses} déjà repris antérieurement).",
            integrationEvent.OrderId, integrationEvent.Reason,
            gains.Count - refuses, gains.Count, refuses);
    }
}
