using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;

namespace HBA.Financial.Wallet.Application.Earnings;

/// <summary>
/// À la livraison confirmée de TOUTE la commande, libère les gains restants
/// (escrow levé) : passage « Accrued » → « Released », et déplacement du net
/// correspondant du solde à venir vers le solde principal de chaque vendeur.
/// Filet de sécurité complémentaire du handler par expédition : idempotent, il
/// ne traite que les gains encore « Accrued » (les autres ont déjà été déplacés).
/// </summary>
public sealed class ReleaseEarningsOnOrderDeliveredHandler : IIntegrationEventHandler<OrderDeliveredIntegrationEvent>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly IWalletUnitOfWork _unitOfWork;
    private readonly WalletMutations _wallets;

    public ReleaseEarningsOnOrderDeliveredHandler(
        ISellerEarningRepository earningRepository,
        IWalletUnitOfWork unitOfWork,
        WalletMutations wallets)
    {
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
        _wallets = wallets;
    }

    public async Task HandleAsync(OrderDeliveredIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var earnings = await _earningRepository.ListByOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (earnings.Count == 0)
        {
            return;
        }

        // Regroupe le net réellement libéré (Accrued → Released) par vendeur.
        //
        // LE NET RESTANT : voir l'encadré jumeau de
        // `ReleaseSellerEarningsOnShipmentDeliveredHandler`. Une reprise antérieure a
        // déjà prélevé sur le solde à venir, c'est-à-dire sur ce qu'on bascule ici.
        var releasedNetBySeller = new Dictionary<Guid, decimal>();
        var currencyBySeller = new Dictionary<Guid, string>();
        foreach (var earning in earnings)
        {
            if (earning.Status == EarningStatus.Accrued)
            {
                earning.Release();
                releasedNetBySeller[earning.SellerId] = releasedNetBySeller.GetValueOrDefault(earning.SellerId) + earning.RemainingNetAmount;
                currencyBySeller[earning.SellerId] = earning.Currency;
            }
        }

        foreach (var (sellerId, net) in releasedNetBySeller)
        {
            await _wallets.ReleaseSellerAsync(sellerId, net, currencyBySeller[sellerId], integrationEvent.OrderId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
