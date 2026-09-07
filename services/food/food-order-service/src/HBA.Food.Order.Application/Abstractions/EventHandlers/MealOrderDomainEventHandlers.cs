using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.FoodOrders.Domain.Orders.Events;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.FoodOrders.Application.Orders.EventHandlers;

/// <summary>
/// Publie « commande de repas passée » — le panier l'écoute pour se clore, le
/// paiement pour démarrer.
/// </summary>
public sealed class MealOrderPlacedDomainEventHandler : IDomainEventHandler<MealOrderPlacedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderPlacedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(
        MealOrderPlacedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderPlacedIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId,
                CartId = domainEvent.CartId,
                TotalAmount = domainEvent.GrandTotal,
                Currency = domainEvent.Currency
            },
            cancellationToken);
}

/// <summary>Publie « commande de repas confirmée », AVEC SES LIGNES.</summary>
public sealed class MealOrderConfirmedDomainEventHandler : IDomainEventHandler<MealOrderConfirmedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderConfirmedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        MealOrderConfirmedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderConfirmedIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId,
                TotalAmount = domainEvent.GrandTotal,
                ShippingFee = domainEvent.ShippingFee,
                Currency = domainEvent.Currency,
                CustomerNote = domainEvent.CustomerNote,
                DeliveryQuoteId = domainEvent.DeliveryQuoteId,
                Lines = domainEvent.Lines
                    .Select(l => new MealOrderLinePayload
                    {
                        LineId = l.LineId,
                        MenuItemId = l.MenuItemId,
                        Name = l.Name,
                        Quantity = l.Quantity,
                        UnitPrice = l.UnitPrice,
                        Notes = l.Notes,
                        Options = l.Options
                            .Select(o => new MealOrderLineOptionPayload
                            {
                                OptionGroupId = o.GroupId,
                                OptionId = o.OptionId
                            })
                            .ToList()
                    })
                    .ToList()
            },
            cancellationToken);
}

/// <summary>Publie « commande de repas annulée ».</summary>
public sealed class MealOrderCancelledDomainEventHandler : IDomainEventHandler<MealOrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(
        MealOrderCancelledDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderCancelledIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « commande en arbitrage ».</summary>
public sealed class MealOrderUnderReviewDomainEventHandler
    : IDomainEventHandler<MealOrderUnderReviewDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderUnderReviewDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        MealOrderUnderReviewDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderUnderReviewIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId,
                Reason = domainEvent.Reason
            },
            cancellationToken);
}

/// <summary>Publie « arbitrage levé, la commande repart ».</summary>
public sealed class MealOrderResumedAfterReviewDomainEventHandler
    : IDomainEventHandler<MealOrderResumedAfterReviewDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderResumedAfterReviewDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        MealOrderResumedAfterReviewDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderResumedAfterReviewIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId,
                PreviousReason = domainEvent.PreviousReason
            },
            cancellationToken);
}

/// <summary>Publie « repas livré » : escrow à libérer, restaurateur à régler.</summary>
public sealed class MealOrderDeliveredDomainEventHandler : IDomainEventHandler<MealOrderDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public MealOrderDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(
        MealOrderDeliveredDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new MealOrderDeliveredIntegrationEvent
            {
                OrderId = domainEvent.OrderId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId
            },
            cancellationToken);
}
