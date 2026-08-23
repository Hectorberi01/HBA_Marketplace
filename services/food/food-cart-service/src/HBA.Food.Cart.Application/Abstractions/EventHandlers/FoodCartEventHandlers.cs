using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Contracts.IntegrationEvents;
using HBA.FoodCarts.Domain.Carts;
using HBA.FoodCarts.Domain.Carts.Events;
using HBA.FoodOrders.Contracts.IntegrationEvents;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;

namespace HBA.FoodCarts.Application.Carts.EventHandlers;

/// <summary>
/// Chorégraphie : la commande de repas est partie, le panier se clôt.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PANIER SE CLÔT SUR « COMMANDE PASSÉE », PAS À LA DEMANDE D'UN CLIENT.
///
/// C'est le même choix que du côté marketplace, et pour la même raison : si le
/// panier se vidait au clic sur « commander » et que la création de la commande
/// échouait ensuite — carte refusée, restaurant fermé entre-temps, devis de
/// course indisponible — le client se retrouverait sans panier ET sans commande,
/// à devoir tout ressaisir.
///
/// ET IL VÉRIFIE QUE LE PANIER EST ENCORE ACTIF.
///
/// L'événement peut être rejoué : Kafka garantit « au moins une fois ». Sans ce
/// test, un rejeu appellerait `MarkCheckedOut` sur un panier déjà clos, qui
/// rendrait un échec silencieusement ignoré — et surtout referait tomber le
/// cache d'un panier neuf que le client aurait commencé entre-temps.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
