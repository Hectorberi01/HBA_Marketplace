using HBA.Shared.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
using HBA.Shipping.Contracts.IntegrationEvents;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// Affinage multi-vendeur : à la livraison de l'expédition d'UN vendeur, on libère
/// ses gains sur la commande (escrow levé) PUIS on déplace le NET correspondant de
/// son « solde à venir » vers son « solde principal » (retirable).
///
/// Aucun payout automatique : le vendeur déclenchera lui-même un retrait depuis
/// son solde principal. Idempotent : seuls les gains qui passent réellement de
/// Accrued à Released dans cet appel alimentent le déplacement de solde.
/// </summary>
public sealed class ReleaseSellerEarningsOnShipmentDeliveredHandler : IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly WalletMutations _wallets;

    public ReleaseSellerEarningsOnShipmentDeliveredHandler(
        ISellerEarningRepository earningRepository,
        IWalletUnitOfWork unitOfWork,
        WalletMutations wallets)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
        _wallets = wallets;
    }

    public async Task HandleAsync(ShipmentDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        var sellerEarnings = earnings.Where(e => e.SellerId == integrationEvent.SellerId).ToList();
        if (sellerEarnings.Count == 0)
        {
            return;
        }

        // Ne déplace que le net qui bascule effectivement Accrued → Released ici.
        //
        // ET SEULEMENT CE QUI RESTE DÛ. Un gain peut avoir été partiellement REPRIS
        // avant sa livraison (retour d'un colis d'une commande multi-envois) :
        // `DebitForRefund` a alors déjà pris sur le solde À VENIR, celui-là même qu'on
        // bascule ici. Déplacer le net d'ORIGINE tenterait de sortir de l'en-cours une
        // somme qui n'y est plus — `ReleaseToAvailable` la raboterait silencieusement
        // au solde réel, et le vendeur verrait son disponible sous-alimenté sans que
        // rien ne l'explique.
        var releasedNet = 0m;
        var currency = sellerEarnings[0].Currency;
        foreach (var earning in sellerEarnings)
        {
            if (earning.Status == EarningStatus.Accrued)
            {
                earning.Release();
                releasedNet += earning.RemainingNetAmount;
            }
        }

        if (releasedNet > 0m)
        {
            await _wallets.ReleaseSellerAsync(integrationEvent.SellerId, releasedNet, currency, integrationEvent.OrderId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
