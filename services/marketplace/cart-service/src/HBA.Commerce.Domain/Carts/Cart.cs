using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Domain.Carts.Events;

namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// Panier d'un acheteur. Garde un snapshot des lignes (offre + prix de base) ; le
/// prix effectif avec promotions est calculé à la volée par Pricing.
/// </summary>
public sealed class Cart : AggregateRoot<CartId>
{
    private readonly List<CartItem> _items = new();

    private Cart(){}

    private Cart(CartId id, Guid buyerId, string currency): base(id)
    {
        BuyerId = buyerId;
        Currency = currency;
        Status = CartStatus.Active;

        Raise(new CartCreatedDomainEvent(id.Value, buyerId));
    }

    public Guid BuyerId { get; private set; }
    public string Currency { get; private set; } = default!;
    public CartStatus Status { get; private set; }

    /// <summary>Code promo saisi par l'acheteur, ou null.</summary>
    public string? PromotionCode { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items.AsReadOnly();

    public static Result<Cart> Create(Guid buyerId, string currency)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("cart.buyer_required", "L'acheteur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("cart.currency_invalid", "La devise doit être un code ISO à 3 lettres.");
        }

        return new Cart(CartId.New(), buyerId, currency.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// La nature des lignes déjà présentes, ou <c> null</c> si le panier est vide.
    /// </summary>
    public CartLineKind? Kind => _items.Count == 0 ? null : _items[0].Kind;

    public Result AddItem(
        Guid offerId, Guid productId, Guid categoryId, Guid sellerId, string sku,
        Guid shipFromLocationId, decimal unitBaseAmount, string currency, int quantity)
    {
        var garde = VerifierAjout(CartLineKind.Goods, currency, quantity);
        if (garde.IsFailure)
        {
            return garde;
        }

        // ON NE CHERCHE QUE PARMI LES LIGNES DE MARCHANDISE.
        var existing = _items.FirstOrDefault(i => i.Kind == CartLineKind.Goods && i.OfferId == offerId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new CartItem(
                Guid.NewGuid(), offerId, productId, categoryId, sellerId, sku,
                shipFromLocationId, unitBaseAmount, Currency, quantity));
        }

        Raise(new ItemAddedToCartDomainEvent(Id.Value, offerId, quantity, CartLineKind.Goods.ToString()));
        return Result.Success();
    }

    /// <summary>Ajoute un plat au panier.</summary>
    public Result AddFoodItem(
        Guid restaurantId, Guid menuItemId, decimal unitBaseAmount, string currency,
        int quantity, string? notes, IReadOnlyList<(Guid GroupId, Guid OptionId)> options)
    {
        var garde = VerifierAjout(CartLineKind.Food, currency, quantity);
        if (garde.IsFailure)
        {
            return garde;
        }

        if (restaurantId == Guid.Empty || menuItemId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "cart.food.item_required", "Le restaurant et le plat sont obligatoires."));
        }

        // AUJOURD'HUI IMPOSSIBLE, DEMAIN PEUT-ÊTRE.
        if (unitBaseAmount < 0m)
        {
            return Result.Failure(Error.Validation(
                "cart.food.price_invalid", "Le prix d'un plat ne peut pas être négatif."));
        }

        // UN PANIER FOOD NE PORTE QU'UN SEUL RESTAURANT.
        var autre = _items.FirstOrDefault(
            i => i.Kind == CartLineKind.Food && i.RestaurantId != restaurantId);
        if (autre is not null)
        {
            return Result.Failure(Error.Conflict(
                "cart.food.single_restaurant",
                "Votre panier contient déjà des plats d'un autre restaurant."));
        }

        var choisies = options.Select(o => o.OptionId).ToList();
        var existing = _items.FirstOrDefault(i => i.MatchesFood(menuItemId, choisies));

        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
        }
        else
        {
            _items.Add(new CartItem(
                Guid.NewGuid(), restaurantId, menuItemId, unitBaseAmount, Currency, quantity, notes, options));
        }

        // L'ÉVÉNEMENT PORTE UN PLAT DANS UN CHAMP NOMMÉ « ARTICLE ».
        Raise(new ItemAddedToCartDomainEvent(Id.Value, menuItemId, quantity, CartLineKind.Food.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Les contrôles communs à tout ajout : panier modifiable, quantité positive,
    /// devise du panier, et homogénéité des natures.
    /// </summary>
    private Result VerifierAjout(CartLineKind kind, string currency, int quantity)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("cart.quantity_invalid", "La quantité doit être positive."));
        }

        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Conflict("cart.currency_mismatch", "L'article n'est pas dans la devise du panier."));
        }

        if (Kind is { } presente && presente != kind)
        {
            return Result.Failure(Error.Conflict(
                "cart.kind_mismatch",
                presente == CartLineKind.Food
                    ? "Votre panier contient des plats : validez-le avant de commander autre chose."
                    : "Votre panier contient des articles : validez-le avant de commander un repas."));
        }

        return Result.Success();
    }

    public Result UpdateItemQuantity(Guid offerId, int quantity)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        var item = _items.FirstOrDefault(i => i.Kind == CartLineKind.Goods && i.OfferId == offerId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Article absent du panier."));
        }

        if (quantity <= 0)
        {
            _items.Remove(item);
            return Result.Success();
        }

        item.SetQuantity(quantity);
        return Result.Success();
    }

    public Result RemoveItem(Guid offerId)
    {
        var item = _items.FirstOrDefault(i => i.Kind == CartLineKind.Goods && i.OfferId == offerId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Article absent du panier."));
        }

        _items.Remove(item);
        return Result.Success();
    }

    /// <summary>Modifie la quantité d'une ligne désignée par SON identifiant.</summary>
    public Result UpdateLineQuantity(Guid lineId, int quantity)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        var item = _items.FirstOrDefault(i => i.Id == lineId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Ligne absente du panier."));
        }

        if (quantity <= 0)
        {
            _items.Remove(item);
            return Result.Success();
        }

        item.SetQuantity(quantity);
        return Result.Success();
    }

    /// <summary>Retire une ligne désignée par son identifiant.</summary>
    public Result RemoveLine(Guid lineId)
    {
        var item = _items.FirstOrDefault(i => i.Id == lineId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("cart.item.not_found", "Ligne absente du panier."));
        }

        _items.Remove(item);
        return Result.Success();
    }

    public void Clear() => _items.Clear();

    /// <summary>Applique un code promo au panier.</summary>
    public Result ApplyPromotionCode(string code)
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation("cart.promotion_code_required", "Le code promo est obligatoire."));
        }

        PromotionCode = code.Trim().ToUpperInvariant();
        return Result.Success();
    }

    /// <summary>Retire le code promo du panier.</summary>
    public Result RemovePromotionCode()
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus modifiable."));
        }

        PromotionCode = null;
        return Result.Success();
    }

    public Result MarkCheckedOut()
    {
        if (Status != CartStatus.Active)
        {
            return Result.Failure(Error.Conflict("cart.not_active", "Le panier n'est plus actif."));
        }

        if (_items.Count == 0)
        {
            return Result.Failure(Error.Conflict("cart.empty", "Impossible de valider un panier vide."));
        }

        Status = CartStatus.CheckedOut;
        Raise(new CartCheckedOutDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }
}
