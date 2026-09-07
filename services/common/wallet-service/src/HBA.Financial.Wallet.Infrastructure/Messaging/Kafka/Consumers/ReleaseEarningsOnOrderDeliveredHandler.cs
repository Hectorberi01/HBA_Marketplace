using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// À la livraison confirmée de TOUTE la commande, libère les gains restants (escrow
/// levé) : passage « Accrued » → « Released », et déplacement du net correspondant
/// du solde à venir vers le solde principal de chaque vendeur.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Financial.Wallet.Application.Earnings.ReleaseEarningsOnOrderDeliveredHandler")]
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
