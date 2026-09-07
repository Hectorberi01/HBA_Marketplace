using HBA.Products.Contracts;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Application.Carts;
using HBA.Commerce.Domain.Carts;
using HBA.Inventory.Contracts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Application.Carts.Commands.AddItem;

/// <summary>AJOUT D'UNE LIGNE AU PANIER.</summary>
internal sealed class AddItemToCartCommandHandler : ICommandHandler<AddItemToCartCommand, Guid>
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductsModuleApi _products;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly ICartUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public AddItemToCartCommandHandler(
        ICartRepository cartRepository,
        IProductsModuleApi products,
        IInventoryModuleApi inventoryModuleApi,
        ICartUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _cartRepository = cartRepository;
        _products = products;
        _inventoryModuleApi = inventoryModuleApi;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(AddItemToCartCommand command, CancellationToken cancellationToken)
    {
        // CETTE LECTURE PEUT ÊTRE INDISPONIBLE, ET IL FAUT LE DIRE.
        OfferSummary? offer;

        try
        {
            offer = await _products.GetOfferAsync(command.OfferId, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // `Error.Failure` et non `NotFound` : la nuance est le fond du sujet.
            return Result.Failure<Guid>(Error.Failure(
                "cart.catalog_unavailable",
                "Le catalogue des offres n'est pas disponible sur cette installation."));
        }

        if (offer is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.offer.not_found", "Offre introuvable."));
        }

        // ON LIT UN DRAPEAU, PLUS UNE CHAÎNE.
        if (!offer.IsPurchasable)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "cart.offer.not_active", "L'offre n'est pas disponible à la vente."));
        }

        var product = await _products.GetProductAsync(offer.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.product.not_found", "Produit introuvable."));
        }

        // CONTRÔLE NOUVEAU : LA FICHE DOIT ÊTRE VISIBLE.
        if (!product.IsVisible)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "cart.product.not_available", "Ce produit n'est plus proposé à la vente."));
        }

        // Le SKU rattache la ligne au stock.
        if (string.IsNullOrWhiteSpace(offer.Sku))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "cart.offer.without_sku",
                "Cette offre n'a pas de référence de stock et ne peut pas être commandée."));
        }

        if (!await _inventoryModuleApi.IsInStockAsync(offer.Sku, command.Quantity, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("cart.out_of_stock", "Stock insuffisant pour cette quantité."));
        }

        var cart = await _cartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            var created = CartAggregate.Create(command.BuyerId, offer.Currency);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            cart = created.Value;
            await _cartRepository.AddAsync(cart, cancellationToken);
        }

        // EffectivePrice et non BuyerPrice : c'est le prix promotionnel s'il court,
        // le prix courant sinon.
        var addResult = cart.AddItem(
            offer.Id, offer.ProductId, product.CategoryId, offer.SellerId, offer.Sku,
            offer.ShipFromLocationId, offer.EffectivePrice, offer.Currency, command.Quantity);

        if (addResult.IsFailure)
        {
            return Result.Failure<Guid>(addResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(command.BuyerId), cancellationToken);

        return cart.Id.Value;
    }
}
