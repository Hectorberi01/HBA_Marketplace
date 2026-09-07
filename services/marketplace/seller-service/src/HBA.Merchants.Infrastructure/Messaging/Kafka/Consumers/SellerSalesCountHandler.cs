using HBA.Merchants.Application.Abstractions;
using HBA.Merchants.Domain.Sellers;
using HBA.Ordering.Contracts;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Context;
using HBA.Shared.IntegrationEvents;
using Microsoft.Extensions.Logging;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Merchants.Infrastructure;

using HBA.Merchants.Infrastructure.Persistence.Outbox;
using HBA.Merchants.Infrastructure.Persistence.Inbox;
using HBA.Shared.Infrastructure.Events;
namespace HBA.Merchants.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>LE COMPTEUR DE VENTES, ENFIN ALIMENTÉ.</summary>
public sealed class SellerSalesCountHandler
    : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    /// <summary>Nom de ce consumer dans `consumer_inbox` (§19.5).</summary>
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
