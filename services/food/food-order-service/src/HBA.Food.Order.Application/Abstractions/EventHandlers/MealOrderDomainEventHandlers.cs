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

/// <summary>
/// Publie « commande de repas confirmée », AVEC SES LIGNES.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE GESTIONNAIRE NE RELIT RIEN — IL RECOPIE.
///
/// Les lignes voyagent sur l'événement de domaine, construites par `Confirm()`.
/// Les relire ici depuis le dépôt marcherait — l'entité est encore suivie — mais
/// ferait dépendre la charge utile publiée de l'état de la base au moment du
/// dispatch, et non de l'état qui a MOTIVÉ la confirmation. Ce sont deux choses
/// différentes dès qu'une seconde écriture s'intercale.
///
/// Le chemin qu'on remplace faisait bien pire : `OrderConfirmed` partait sans
/// lignes, le pont vers Food testait `Kind == "Food"`, RAPPELAIT order-service
/// par gRPC pour les obtenir, puis refiltrait sur `Kind`. Trois pas et un
/// aller-retour réseau, dont aucun n'existait pour une bonne raison — seulement
/// parce que l'événement servait deux univers et ne pouvait donc rien porter de
/// spécifique à l'un.
///
/// La recopie en deux types — un du domaine, un du contrat — est délibérée : le
/// contrat public ne doit pas dépendre du modèle interne.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
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

/// <summary>Publie « commande de repas annulée ». financial-service rembourse en la consommant.</summary>
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

/// <summary>
/// Publie « commande en arbitrage ».
/// </summary>
/// <remarks>
/// SANS CE PUBLICATEUR, LE CLIENT NE SAURAIT TOUJOURS RIEN.
///
/// C'est tout l'objet de la transition : une commande devenue inexécutable
/// restait confirmée sans un mot, et le client découvrait le problème en n'ayant
/// rien reçu. Le fait doit sortir du service pour que quelqu'un le lui dise.
/// </remarks>
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
