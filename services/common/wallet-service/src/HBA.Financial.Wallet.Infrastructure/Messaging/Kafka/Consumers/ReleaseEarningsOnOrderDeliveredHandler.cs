using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Financial.Wallet.Application.Abstractions;
using HBA.Financial.Wallet.Application.Wallets;
using HBA.Financial.Wallet.Domain.Earnings;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
//
// Il vivait dans `HBA.Financial.Wallet.Application.Earnings` et y resolvait ses voisins SANS `using` : le
// compilateur cherche d'abord dans les espaces de noms englobants. Descendu
// dans `Messaging/Kafka/Consumers`, il a perdu ce voisinage — d'ou les lignes
// ci-dessous, qui rendent explicite ce qui etait implicite.
using HBA.Financial.Wallet.Application.Earnings;

namespace HBA.Financial.Wallet.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// À la livraison confirmée de TOUTE la commande, libère les gains restants
/// (escrow levé) : passage « Accrued » → « Released », et déplacement du net
/// correspondant du solde à venir vers le solde principal de chaque vendeur.
/// ═════════════════════════════════════════════════════════════════════════════
/// CE GESTIONNAIRE N'EST PLUS UN FILET : IL EST LE SEUL CHEMIN.
///
/// Il se décrivait comme le « filet de sécurité complémentaire du handler par
/// expédition ». Ce handler-là — `ReleaseSellerEarningsOnShipmentDeliveredHandler`
/// — a été SUPPRIMÉ : il attendait `ShipmentDeliveredIntegrationEvent`, qu'aucun
/// service du dépôt n'a jamais publié. Il était enregistré, correct, et n'a jamais
/// tourné une seule fois.
///
/// Il reste idempotent, et c'est ce qui rend la suppression sans effet : il ne
/// traite que les gains encore « Accrued », les autres ayant déjà été déplacés.
///
/// CE QUE LA PLATEFORME A PERDU, ET IL FAUT LE SAVOIR : la libération PAR
/// EXPÉDITION dans une commande multi-vendeur. Un vendeur qui livre en premier
/// attend désormais que TOUTE la commande soit livrée pour être réglé. Ce n'est
/// pas une régression — c'est déjà le comportement réel, puisque l'autre chemin
/// n'a jamais existé — mais c'est une fonctionnalité à re-créer si le multi-vendeur
/// partiel devient un besoin.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
//
// `IntegrationEventDispatcher` la derivait du nom complet du type. Descendre ce
// fichier dans `Messaging/Kafka/Consumers` a change son espace de noms, donc sa
// cle, donc a orpheline ses traces dans `consumer_inbox` : au premier rejeu,
// chaque evenement deja traite serait repasse pour neuf.
//
// Les valeurs ci-dessous reproduisent le nom complet d'AVANT le deplacement.
// Ce sont des cles de base de donnees : elles ne se refactorisent pas.
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
        //
        // LE NET RESTANT. Une reprise antérieure a déjà prélevé sur le solde à
        // venir, c'est-à-dire sur ce qu'on bascule ici.
        //
        // L'encadré « jumeau » auquel cette ligne renvoyait vivait dans
        // `ReleaseSellerEarningsOnShipmentDeliveredHandler`, supprimé : renvoyer à
        // un fichier absent est pire que ne rien dire.
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
