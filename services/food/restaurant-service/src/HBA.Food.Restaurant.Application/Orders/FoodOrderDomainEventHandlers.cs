using HBA.Food.Contracts.IntegrationEvents;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Orders.Events;
using static HBA.Food.Application.Orders.FoodOrderOriginTranslation;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.Food.Application.Orders;

/// <summary>LES HUIT PUBLICATEURS DE LA COMMANDE FOOD (cahier §19).</summary>
public sealed class FoodOrderReceivedDomainEventHandler : IDomainEventHandler<FoodOrderReceivedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderReceivedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderReceivedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderReceivedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Total = e.Total,
                ItemCount = e.ItemCount
            },
            cancellationToken);
}

public sealed class FoodOrderAcceptedDomainEventHandler : IDomainEventHandler<FoodOrderAcceptedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderAcceptedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderAcceptedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderAcceptedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                EstimatedPreparationMinutes = e.EstimatedPreparationMinutes,
                AcceptedByUserId = e.AcceptedByUserId
            },
            cancellationToken);
}

/// <summary>LE PLUS URGENT DES SEPT : LE CLIENT A PAYÉ.</summary>
public sealed class FoodOrderRejectedDomainEventHandler : IDomainEventHandler<FoodOrderRejectedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderRejectedDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderRejectedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderRejectedIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Reason = e.Reason,
                Comment = e.Comment
            },
            cancellationToken);
}

public sealed class FoodOrderPreparationStartedDomainEventHandler
    : IDomainEventHandler<FoodOrderPreparationStartedDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderPreparationStartedDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        FoodOrderPreparationStartedDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderPreparingIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

/// <summary>CELUI QUI APPELLE UN LIVREUR (§24).</summary>
public sealed class FoodOrderReadyForPickupDomainEventHandler
    : IDomainEventHandler<FoodOrderReadyForPickupDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderReadyForPickupDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        FoodOrderReadyForPickupDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderReadyForPickupIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                ReadyAtUtc = e.ReadyAtUtc
            },
            cancellationToken);
}

public sealed class FoodOrderPickedUpDomainEventHandler : IDomainEventHandler<FoodOrderPickedUpDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderPickedUpDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderPickedUpDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderPickedUpIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

/// <summary>CELUI QUI FAIT PAYER LE RESTAURATEUR.</summary>
public sealed class FoodOrderDeliveredDomainEventHandler : IDomainEventHandler<FoodOrderDeliveredDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderDeliveredDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderDeliveredDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderDeliveredIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId
            },
            cancellationToken);
}

public sealed class FoodOrderCancelledDomainEventHandler : IDomainEventHandler<FoodOrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodOrderCancelledDomainEventHandler(IIntegrationEventPublisher publisher) => _publisher = publisher;

    public Task HandleAsync(FoodOrderCancelledDomainEvent e, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodOrderCancelledIntegrationEvent
            {
                FoodOrderId = e.FoodOrderId,
                OrderId = e.OrderId,
                OrderOrigin = Traduire(e.Origin),
                RestaurantId = e.RestaurantId,
                Reason = e.Reason,
                WasInKitchen = e.WasInKitchen
            },
            cancellationToken);
}

// PUBLIQUE : l'infrastructure s'en sert aussi, pour `FoodModuleApi.GetOrderAsync`.
/// <summary>La traduction domaine → contrat de l'univers de la commande.</summary>
public static class FoodOrderOriginTranslation
{
    public static string Traduire(FoodOrderOrigin origine) => origine switch
    {
        FoodOrderOrigin.Marketplace => FoodOrderOrigins.Marketplace,
        FoodOrderOrigin.Food => FoodOrderOrigins.Food,
        _ => throw new ArgumentOutOfRangeException(
            nameof(origine), origine,
            "Univers de commande inconnu : aucune valeur de contrat ne lui correspond.")
    };
}
