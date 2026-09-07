using HBA.FoodCarts.Domain.Carts.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>
/// Panier de restauration d'un acheteur : les plats d'UN établissement, leurs
/// options et leurs quantités.
/// </summary>
public sealed class FoodCart : AggregateRoot<FoodCartId>
{
    private readonly List<FoodCartItem> _items = new();

    private FoodCart()
    {
    }

    private FoodCart(FoodCartId id, Guid buyerId, Guid restaurantId, string currency)
        : base(id)
    {
        BuyerId = buyerId;
        RestaurantId = restaurantId;
        Currency = currency;
        Status = FoodCartStatus.Active;

        Raise(new FoodCartCreatedDomainEvent(id.Value, buyerId));
    }

    public Guid BuyerId { get; private set; }

    /// <summary>L'établissement du panier.</summary>
    public Guid RestaurantId { get; private set; }

    public string Currency { get; private set; } = default!;

    public FoodCartStatus Status { get; private set; }

    /// <summary>Code promo saisi par l'acheteur, ou null.</summary>
    public string? PromotionCode { get; private set; }

    public IReadOnlyCollection<FoodCartItem> Items => _items.AsReadOnly();

    /// <summary>Ouvre un panier pour un acheteur, chez un établissement, dans une devise.</summary>
    public static Result<FoodCart> Create(Guid buyerId, Guid restaurantId, string currency)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("food_cart.buyer_required", "L'acheteur est obligatoire.");
        }

        if (restaurantId == Guid.Empty)
        {
            return Error.Validation("food_cart.restaurant_required", "Le restaurant est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            return Error.Validation("food_cart.currency_invalid", "La devise doit être un code ISO à 3 lettres.");
        }

        return new FoodCart(FoodCartId.New(), buyerId, restaurantId, currency.Trim().ToUpperInvariant());
    }

    /// <summary>Ajoute un plat, ou augmente la quantité de la ligne identique existante.</summary>
    public Result AddItem(
        Guid restaurantId,
        Guid menuItemId,
        string nameSnapshot,
        decimal unitBaseAmount,
        string currency,
        int quantity,
        string? notes,
        IReadOnlyList<(Guid GroupId, Guid OptionId)> options)
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("food_cart.quantity_invalid", "La quantité doit être positive."));
        }

        if (menuItemId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("food_cart.item_required", "Le plat est obligatoire."));
        }

        if (restaurantId != RestaurantId)
        {
            return Result.Failure(Error.Conflict(
                "food_cart.single_restaurant",
                "Votre panier contient déjà des plats d'un autre restaurant."));
        }

        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Conflict(
                "food_cart.currency_mismatch", "Ce plat n'est pas dans la devise du panier."));
        }

        // AUJOURD'HUI IMPOSSIBLE, DEMAIN PEUT-ÊTRE.
        if (unitBaseAmount < 0m)
        {
            return Result.Failure(Error.Validation(
                "food_cart.price_invalid", "Le prix d'un plat ne peut pas être négatif."));
        }

        var choisies = options.Select(o => o.OptionId).ToList();
        var existante = _items.FirstOrDefault(i => i.Matches(menuItemId, choisies));

        if (existante is not null)
        {
            existante.IncreaseQuantity(quantity);
            existante.RefreshUnitPrice(unitBaseAmount, nameSnapshot);
        }
        else
        {
            _items.Add(new FoodCartItem(
                Guid.NewGuid(), menuItemId, nameSnapshot, unitBaseAmount, Currency, quantity, notes, options));
        }

        Raise(new FoodItemAddedToCartDomainEvent(Id.Value, RestaurantId, menuItemId, quantity));
        return Result.Success();
    }

    /// <summary>Modifie la quantité d'une ligne désignée par SON identifiant.</summary>
    public Result UpdateLineQuantity(Guid lineId, int quantity)
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        var ligne = _items.FirstOrDefault(i => i.Id == lineId);
        if (ligne is null)
        {
            return Result.Failure(Error.NotFound("food_cart.line.not_found", "Ligne absente du panier."));
        }

        if (quantity <= 0)
        {
            _items.Remove(ligne);
            return Result.Success();
        }

        ligne.SetQuantity(quantity);
        return Result.Success();
    }

    /// <summary>Retire une ligne désignée par son identifiant.</summary>
    public Result RemoveLine(Guid lineId)
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        var ligne = _items.FirstOrDefault(i => i.Id == lineId);
        if (ligne is null)
        {
            return Result.Failure(Error.NotFound("food_cart.line.not_found", "Ligne absente du panier."));
        }

        _items.Remove(ligne);
        return Result.Success();
    }

    public Result Clear()
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        _items.Clear();
        return Result.Success();
    }

    /// <summary>
    /// Applique un code promo. Normalisé (trim + MAJUSCULES), comme à la création
    /// d'une promotion — sans quoi « bienvenue10 » ne trouverait jamais «
    /// BIENVENUE10 ».
    /// </summary>
    public Result ApplyPromotionCode(string code)
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure(Error.Validation(
                "food_cart.promotion_code_required", "Le code promo est obligatoire."));
        }

        PromotionCode = code.Trim().ToUpperInvariant();
        return Result.Success();
    }

    public Result RemovePromotionCode()
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus modifiable."));
        }

        PromotionCode = null;
        return Result.Success();
    }

    public Result MarkCheckedOut()
    {
        if (Status != FoodCartStatus.Active)
        {
            return Result.Failure(Error.Conflict("food_cart.not_active", "Le panier n'est plus actif."));
        }

        if (_items.Count == 0)
        {
            return Result.Failure(Error.Conflict("food_cart.empty", "Impossible de valider un panier vide."));
        }

        Status = FoodCartStatus.CheckedOut;
        Raise(new FoodCartCheckedOutDomainEvent(Id.Value, BuyerId, RestaurantId));
        return Result.Success();
    }
}
