using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>UNE COMMANDE TOMBE APRÈS AVOIR ÉTÉ COMPTABILISÉE — ON REPREND TOUT.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Wallet.Application.Earnings.ReverseEarningsOnOrderCancelledHandler")]
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
        var gains = await _earnings.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);

        if (gains.Count == 0)
        {
            return;
        }

        // VERROU D'IDEMPOTENCE. NE PAS RETIRER.
        if (await _wallets.RefundAlreadyReversedAsync(integrationEvent.OrderId, cancellationToken))
        {
            _logger.LogInformation(
                "Commande {OrderId} : contre-passation DÉJÀ effectuée — rejeu ignoré.",
                integrationEvent.OrderId);

            return;
        }

        // ON REPREND LES MONTANTS RÉELLEMENT ÉCRITS, PAS UN CALCUL REFAIT.
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

        // LES FRAIS DE LIVRAISON AUSSI, ET C'EST LE POINT LE PLUS OUBLIÉ.
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
        _logger.LogInformation(
            "Commande {OrderId} annulée ({Reason}) : {Repris} gain(s) contre-passé(s) sur {Total} "
            + "({Refuses} déjà repris antérieurement).",
            integrationEvent.OrderId, integrationEvent.Reason,
            gains.Count - refuses, gains.Count, refuses);
    }
}
