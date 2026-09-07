using HBA.Shared.Application.Abstractions;
using HBA.Shared.IntegrationEvents;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Domain.Carts;
using HBA.Orders.Contracts.IntegrationEvents;
// LES ESPACES DE NOMS QUE CE FICHIER HABITAIT, DEVENUS DES `using`.
using HBA.Commerce.Application.Carts;
using HBA.Commerce.Application.Carts.EventHandlers;

namespace HBA.Commerce.Infrastructure.Messaging.Kafka.Consumers;

/// <summary>
/// Réaction (chorégraphie) à « commande placée » : clôt le panier source pour
/// éviter une double commande.
/// </summary>
// LA CLE D'IDEMPOTENCE DE CE FICHIER EST FIGEE, PAS DEDUITE.
[NomDeConsommateur("HBA.Commerce.Application.Carts.EventHandlers.CloseCartOnOrderPlacedHandler")]
public sealed class CloseCartOnOrderPlacedHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CloseCartOnOrderPlacedHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
    {
        _cartRepository = cartRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task HandleAsync(OrderPlacedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var cart = await _cartRepository.GetByIdAsync(new CartId(integrationEvent.CartId), cancellationToken);
        if (cart is null || cart.Status != CartStatus.Active)
        {
            return;
        }

        cart.MarkCheckedOut();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(integrationEvent.BuyerId), cancellationToken);
    }
}
