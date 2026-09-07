using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Contracts.IntegrationEvents;
using HBA.FoodCarts.Domain.Carts;
using HBA.FoodCarts.Domain.Carts.Events;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.FoodCarts.Application.Carts;

namespace HBA.FoodCarts.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>Chorégraphie : la commande de repas est partie, le panier se clôt.</summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.FoodCarts.Application.Carts.EventHandlers.CloseFoodCartOnMealOrderPlacedHandler")]
public sealed class CloseFoodCartOnMealOrderPlacedHandler
    : IIntegrationEventHandler<MealOrderPlacedIntegrationEvent>
{
    private readonly IFoodCartRepository _carts;
    private readonly IFoodCartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CloseFoodCartOnMealOrderPlacedHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task HandleAsync(
        MealOrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var cart = await _carts.GetByIdAsync(new FoodCartId(integrationEvent.CartId), cancellationToken);
        if (cart is null || cart.Status != FoodCartStatus.Active)
        {
            return;
        }

        cart.MarkCheckedOut();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(FoodCartCacheKeys.Active(integrationEvent.BuyerId), cancellationToken);
    }
}

/// <summary>Publie l'événement d'intégration « panier de repas clos » (analytique).</summary>
public sealed class FoodCartCheckedOutDomainEventHandler
    : IDomainEventHandler<FoodCartCheckedOutDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;

    public FoodCartCheckedOutDomainEventHandler(IIntegrationEventPublisher publisher)
        => _publisher = publisher;

    public Task HandleAsync(
        FoodCartCheckedOutDomainEvent domainEvent, CancellationToken cancellationToken = default)
        => _publisher.PublishAsync(
            new FoodCartCheckedOutIntegrationEvent
            {
                CartId = domainEvent.CartId,
                BuyerId = domainEvent.BuyerId,
                RestaurantId = domainEvent.RestaurantId
            },
            cancellationToken);
}
