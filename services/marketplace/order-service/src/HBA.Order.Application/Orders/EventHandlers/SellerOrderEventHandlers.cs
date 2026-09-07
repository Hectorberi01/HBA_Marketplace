using Microsoft.Extensions.Logging;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Orders.Domain.Orders.Events;
using HBA.Orders.Domain.Orders.SellerOrders;
using HBA.Orders.Domain.Orders.SellerOrders.Events;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>Publie « la part d'un vendeur ne sera pas honorée ».</summary>
public sealed class SellerOrderRefusedDomainEventHandler
    : IDomainEventHandler<SellerOrderRefusedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<SellerOrderRefusedDomainEventHandler> _logger;

    public SellerOrderRefusedDomainEventHandler(
        IIntegrationEventPublisher publisher, ILogger<SellerOrderRefusedDomainEventHandler> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    public async Task HandleAsync(
        SellerOrderRefusedDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _publisher.PublishAsync(
            new SellerOrderRefusedIntegrationEvent
            {
                SellerOrderId = domainEvent.SellerOrderId,
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                SellerId = domainEvent.SellerId,
                Currency = domainEvent.Currency,
                Outcome = domainEvent.Outcome,
                Reason = domainEvent.Reason,
                Amount = domainEvent.Amount,

                // Deux records portent la même idée — l'un dans le domaine, l'autre
                // dans les Contracts — et c'est délibéré, comme pour
                // `OrderSellerShare` : le contrat public ne doit pas dépendre du
                // modèle interne d'Ordering.
                Lines = domainEvent.Lines
                    .Select(l => new HBA.Orders.Contracts.IntegrationEvents.SellerOrderRefusedLine(
                        l.OrderLineId, l.ProductId, l.Sku, l.ShipFromLocationId, l.Quantity, l.LineTotal))
                    .ToList()
            },
            cancellationToken);

        // JOURNAL D'AVERTISSEMENT, PAS D'INFORMATION, ET IL RESTE JUSQU'À CE QUE LE
        // PREMIER CONSOMMATEUR EXISTE.
        _logger.LogWarning(
            "Commande vendeur {SellerOrderId} ({Outcome}) : le vendeur {SellerId} n'honore pas sa part "
            + "de la commande {OrderId} — {Amount} {Currency} déjà encaissés. Motif : {Reason}. "
            + "AUCUN CONSOMMATEUR n'écoute encore cet événement : ni stock rendu, ni remboursement, "
            + "ni notification. Reprise MANUELLE requise.",
            domainEvent.SellerOrderId, domainEvent.Outcome, domainEvent.SellerId, domainEvent.OrderId,
            domainEvent.Amount, domainEvent.Currency, domainEvent.Reason);
    }
}

/// <summary>La commande entière tombe : ses parts vendeur tombent avec elle.</summary>
public sealed class CancelSellerOrdersOnOrderCancelledHandler
    : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly ISellerOrderRepository _sellerOrders;
    private readonly ILogger<CancelSellerOrdersOnOrderCancelledHandler> _logger;

    public CancelSellerOrdersOnOrderCancelledHandler(
        ISellerOrderRepository sellerOrders, ILogger<CancelSellerOrdersOnOrderCancelledHandler> logger)
    {
        _sellerOrders = sellerOrders;
        _logger = logger;
    }

    public async Task HandleAsync(
        OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var parts = await _sellerOrders.ListByOrderAsync(domainEvent.OrderId, cancellationToken);

        foreach (var part in parts.Where(p => p.IsOpen))
        {
            // Le motif de la commande est recopié tel quel : c'est la CAUSE, et
            // c'est ce que le vendeur doit lire dans son carnet.
            var resultat = part.CancelWithOrder(domainEvent.Reason, DateTime.UtcNow);

            if (resultat.IsFailure)
            {
                // ON NE LÈVE PAS : L'ANNULATION DE LA COMMANDE EST ACQUISE.
                _logger.LogError(
                    "Commande {OrderId} annulée, mais la part du vendeur {SellerId} n'a pas pu être "
                    + "fermée ({Code}). Elle reste ouverte dans son carnet : fermeture manuelle requise.",
                    domainEvent.OrderId, part.SellerId, resultat.Error.Code);
            }
        }
    }
}
