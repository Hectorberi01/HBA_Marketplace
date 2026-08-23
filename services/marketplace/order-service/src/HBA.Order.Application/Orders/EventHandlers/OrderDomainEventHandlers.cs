using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Orders.Contracts.IntegrationEvents;
using HBA.Orders.Domain.Orders.Events;

namespace HBA.Orders.Application.Orders.EventHandlers;

/// <summary>Publie l'IntegrationEvent « commande placée » (Cart clôture, Payments démarre).</summary>
public sealed class OrderPlacedDomainEventHandler : IDomainEventHandler<OrderPlacedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderPlacedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderPlacedIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                CartId = domainEvent.CartId,
                GrandTotal = domainEvent.GrandTotal,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande confirmée » (Shipping, Notifications).</summary>
public sealed class OrderConfirmedDomainEventHandler : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderConfirmedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderConfirmedIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                Currency = domainEvent.Currency,
                PromotionCode = domainEvent.PromotionCode,

                // La répartition par vendeur traverse la frontière du module. Deux
                // records distincts portent la même idée — l'un dans le domaine,
                // l'autre dans les Contracts — et c'est délibéré : le contrat public
                // ne doit pas dépendre du modèle interne d'Ordering, sans quoi le
                // moindre remaniement du domaine casserait tous les autres modules.
                SellerShares = domainEvent.SellerShares
                    .Select(s => new HBA.Orders.Contracts.IntegrationEvents.OrderSellerShare(
                        s.SellerId, s.ItemCount, s.Amount))
                    .ToList(),

                // SANS CES DEUX CHAMPS, SHIPPING FABRIQUE UN COLIS POUR UN REPAS.
                Kind = domainEvent.Kind,
                RestaurantId = domainEvent.RestaurantId,
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande annulée » (Notifications, analytics).</summary>
public sealed class OrderCancelledDomainEventHandler : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderCancelledIntegrationEvent { OrderId = domainEvent.OrderId, BuyerId = domainEvent.BuyerId, Reason = domainEvent.Reason },
            cancellationToken);
}

/// <summary>
/// Publie l'IntegrationEvent « commande en arbitrage ».
/// </summary>
/// <remarks>
/// SANS CE PUBLICATEUR, L'ACHETEUR NE SAURAIT TOUJOURS RIEN.
///
/// C'est tout l'objet de la transition : une commande devenue inexécutable
/// restait `Confirmed` sans un mot, et le client découvrait le problème en
/// n'ayant rien reçu au bout de plusieurs jours. Le fait doit sortir du module
/// pour que quelqu'un le lui dise.
/// </remarks>
public sealed class OrderUnderReviewDomainEventHandler : IDomainEventHandler<OrderUnderReviewDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderUnderReviewDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderUnderReviewDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderUnderReviewIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande relancée après arbitrage ».</summary>
public sealed class OrderResumedAfterReviewDomainEventHandler
    : IDomainEventHandler<OrderResumedAfterReviewDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderResumedAfterReviewDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        OrderResumedAfterReviewDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderResumedAfterReviewIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId
            },
            cancellationToken);
}

/// <summary>Publie l'IntegrationEvent « commande livrée » (Payments libère l'escrow, Settlement paie).</summary>
public sealed class OrderDeliveredDomainEventHandler : IDomainEventHandler<OrderDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public OrderDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(OrderDeliveredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new OrderDeliveredIntegrationEvent { OrderId = domainEvent.OrderId, BuyerId = domainEvent.BuyerId },
            cancellationToken);
}
