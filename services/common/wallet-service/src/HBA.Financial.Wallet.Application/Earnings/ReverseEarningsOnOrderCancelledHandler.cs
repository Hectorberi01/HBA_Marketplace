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
        foreach (var gain in gains)
        {
            await _wallets.DebitSellerForRefundAsync(
                gain.SellerId, gain.NetAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);

            await _wallets.DebitPlatformCommissionAsync(
                gain.CommissionAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);

            await _wallets.DebitPlatformProviderFeeAsync(
                gain.ProviderFeeAmount, gain.Currency, integrationEvent.OrderId, cancellationToken);
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

        _logger.LogInformation(
            "Commande {OrderId} annulée ({Reason}) : {Count} gain(s) contre-passé(s).",
            integrationEvent.OrderId, integrationEvent.Reason, gains.Count);
    }
}
