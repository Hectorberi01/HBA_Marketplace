using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Domain.Carts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Application.Carts.Commands.AddFoodItem;

/// <summary>Une option retenue par le client : son groupe et son choix.</summary>
public sealed record FoodOptionChoice(Guid OptionGroupId, Guid OptionId);

/// <summary>AJOUT D'UN PLAT AU PANIER.</summary>
public sealed record AddFoodItemToCartCommand(
    Guid BuyerId,
    Guid RestaurantId,
    Guid MenuItemId,
    decimal UnitBaseAmount,
    string Currency,
    int Quantity,
    string? Notes,
    IReadOnlyList<FoodOptionChoice> Options) : ICommand<Guid>;

internal sealed class AddFoodItemToCartCommandHandler : ICommandHandler<AddFoodItemToCartCommand, Guid>
{
    private readonly ICartRepository _cartRepository;
    private readonly ICartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public AddFoodItemToCartCommandHandler(
        ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
    {
        _cartRepository = cartRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(AddFoodItemToCartCommand command, CancellationToken cancellationToken)
    {
        // CE GESTIONNAIRE N'INTERROGE NI FOOD NI PRICING.
        var cart = await _cartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);

        if (cart is null)
        {
            var creation = CartAggregate.Create(command.BuyerId, command.Currency);
            if (creation.IsFailure)
            {
                return Result.Failure<Guid>(creation.Error);
            }

            cart = creation.Value;
            await _cartRepository.AddAsync(cart, cancellationToken);
        }

        var ajout = cart.AddFoodItem(
            command.RestaurantId,
            command.MenuItemId,
            command.UnitBaseAmount,
            command.Currency,
            command.Quantity,
            command.Notes,
            command.Options.Select(o => (o.OptionGroupId, o.OptionId)).ToList());

        if (ajout.IsFailure)
        {
            return Result.Failure<Guid>(ajout.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // LE CACHE DU PANIER VALORISÉ DOIT TOMBER ICI.
        await _cache.RemoveAsync(CartCacheKeys.Active(command.BuyerId), cancellationToken);

        return Result.Success(cart.Id.Value);
    }
}
