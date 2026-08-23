using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;
using HBA.Ordering.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.Infrastructure.Inbox;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace HBA.Merchants.Infrastructure.Integration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE COMPTEUR DE VENTES, ENFIN ALIMENTÉ.
///
/// RIEN N'INCRÉMENTAIT `SalesCount`. TOUTES LES BOUTIQUES ÉTAIENT À ZÉRO.
///
/// Comme pour la note, la colonne existait, était persistée, et figurait dans la
/// projection de vitrine. `OrderConfirmedIntegrationEvent` listait pourtant
/// « Sellers » parmi ses sept consommateurs prévus — un câblage qui n'existait pas.
///
/// ON RECALCULE DEPUIS LA SOURCE, ON N'INCRÉMENTE PAS.
///
/// C'est la règle que l'agrégat écrit lui-même sur `SetSalesCount` : « poser le
/// total exact est idempotent, alors qu'incrémenter double-compterait si
/// l'événement est rejoué ». L'événement porte pourtant `ItemCount` par vendeur —
/// il aurait suffi de l'ajouter, et c'est exactement le piège. Kafka livre au
/// moins une fois.
///
/// `GetSellerSalesCountAsync` somme les quantités des lignes des commandes
/// `Confirmed` ou `Delivered` — les commandes PAYÉES. Son contrat le dit :
/// « recalculé depuis la source, donc idempotent ».
///
/// CE QU'ON NE COUVRE PAS, ET IL FAUT LE SAVOIR.
///
/// `OrderCancelledIntegrationEvent` NE PORTE AUCUN VENDEUR — ni identifiant, ni
/// parts. Une annulation survenant après la confirmation ne peut donc pas
/// déclencher de recalcul, et le compteur reste trop HAUT jusqu'à la prochaine
/// vente confirmée du même vendeur, qui le remet d'aplomb.
///
/// Le compteur n'est jamais faux par ACCUMULATION — il est seulement en retard à
/// la baisse. Le refermer demande d'ajouter les parts vendeurs à l'événement
/// d'annulation, côté order-service, et c'est un lot à part : son producteur ne
/// tient pas les lignes en main à ce moment-là. Noté dans
/// `AUDIT-SELLER-RESTE.md` §7.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerSalesCountHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5). Stable : il est en base.</summary>
    private const string ConsumerName = "seller-service.order-confirmed-sales-count";

    private readonly ISellerRepository _sellers;
    private readonly IOrderingModuleApi _ordering;
    private readonly IConsumerInbox _inbox;
    private readonly ISellerUnitOfWork _unitOfWork;
    private readonly ILogger<SellerSalesCountHandler> _logger;

    public SellerSalesCountHandler(
        ISellerRepository sellers,
        IOrderingModuleApi ordering,
        IConsumerInbox inbox,
        ISellerUnitOfWork unitOfWork,
        ILogger<SellerSalesCountHandler> logger)
    {
        _sellers = sellers;
        _ordering = ordering;
        _inbox = inbox;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (await _inbox.HasProcessedAsync(e.Id, ConsumerName, cancellationToken))
        {
            return;
        }

        // `SellerShares` EST VIDE PAR CONSTRUCTION POUR UNE COMMANDE DE REPAS.
        //
        // L'événement le dit lui-même, et un restaurant n'est pas un vendeur de la
        // place de marché : son compteur, s'il en faut un, appartient à food. On
        // trace et l'on sort, sans quoi le message reviendrait à chaque rejeu.
        foreach (var part in e.SellerShares)
        {
            var seller = await _sellers.GetByIdAsync(new SellerId(part.SellerId), cancellationToken);

            if (seller is null)
            {
                _logger.LogWarning(
                    "Commande {OrderId} confirmée pour le vendeur {SellerId}, introuvable : "
                    + "compteur de ventes non mis à jour.",
                    e.OrderId, part.SellerId);

                continue;
            }

            var ventes = await _ordering.GetSellerSalesCountAsync(part.SellerId, cancellationToken);

            seller.SetSalesCount(ventes);
        }

        await MarquerTraiteAsync(e, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Task MarquerTraiteAsync(
        OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken)
        => _inbox.MarkProcessedAsync(
            e.Id,
            ConsumerName,
            "order.confirmed",
            HbaRequestContext.Current.CorrelationId,
            cancellationToken);
}
