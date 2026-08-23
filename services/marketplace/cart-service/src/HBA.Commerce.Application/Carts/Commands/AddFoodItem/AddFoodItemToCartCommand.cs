using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Domain.Carts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Application.Carts.Commands.AddFoodItem;

/// <summary>Une option retenue par le client : son groupe et son choix.</summary>
public sealed record FoodOptionChoice(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// AJOUT D'UN PLAT AU PANIER.
///
/// LE PRIX N'EST PAS DANS CETTE COMMANDE, ET IL NE DOIT PAS L'ÊTRE.
///
/// Il est lu dans la carte du restaurant par l'appelant qui voit Cart et Food.
/// Un prix qui voyagerait depuis le client serait un prix qu'on peut réécrire —
/// la même règle que pour le prix acheteur d'une offre marketplace.
///
/// ET IL SERA RECALCULÉ UNE SECONDE FOIS.
///
/// Ce que le panier retient n'est qu'un instantané d'affichage. Food refait le
/// calcul à la réception de la commande, à partir de sa propre carte : un plat
/// dont le prix change entre l'ajout au panier et le paiement est facturé au prix
/// de la carte. Le panier ne fait pas foi, et ne doit pas prétendre le contraire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // La disponibilité du plat, l'appartenance des options à ses groupes, le
        // respect des minimums et maximums, et le prix : tout cela a été vérifié
        // par l'appelant, seul à voir les deux modules. Le panier ne connaît pas la
        // restauration, et lui donner cette dépendance ferait de Cart un module qui
        // sait ce qu'est un plat — exactement ce que la frontière interdit.
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
        //
        // `GetActiveCartQuery` sert une copie mise en cache. Sans cette
        // invalidation, le client ajoute un plat, relit son panier, et ne le voit
        // pas — puis l'y trouve quelques minutes plus tard sans comprendre.
        await _cache.RemoveAsync(CartCacheKeys.Active(command.BuyerId), cancellationToken);

        return Result.Success(cart.Id.Value);
    }
}
