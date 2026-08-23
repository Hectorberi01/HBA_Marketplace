using HBA.FoodCarts.Domain.Carts.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.FoodCarts.Domain.Carts;

/// <summary>
/// Panier de restauration d'un acheteur : les plats d'UN établissement, leurs
/// options et leurs quantités. Agrégat racine — il possède ses lignes.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE PANIER N'EST PLUS CELUI DE LA MARKETPLACE.
///
/// Les deux ont partagé une entité, avec un discriminant `Kind` sur chaque ligne
/// pour dire s'il fallait lire l'offre ou le plat. L'argument d'alors — « ils
/// partagent la quantité, la devise et les totaux » — était vrai et insuffisant :
/// ce qu'ils partageaient était la partie triviale, et ce qui les séparait était
/// la partie qui décide.
///
/// Un panier de marchandise peut porter plusieurs vendeurs, réserve du stock,
/// s'expédie depuis des entrepôts et se paie sur trois jours. Un panier de repas
/// porte UN restaurant, ne réserve rien, se prépare en cuisine et exige une
/// adresse géolocalisée et un devis de course. Aucune de ces règles ne se
/// formulait sans commencer par « si Kind vaut… ».
///
/// La règle qui suit est la démonstration : elle n'a plus besoin d'être écrite.
/// ═════════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// L'établissement du panier. Fixé à l'ouverture, jamais modifié ensuite.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// IL EST SUR LE PANIER, ET PLUS SUR CHAQUE LIGNE.
    ///
    /// L'ancienne version le portait sur la ligne et vérifiait l'unicité en
    /// balayant la collection à chaque ajout : `_items.FirstOrDefault(i =>
    /// i.Kind == Food && i.RestaurantId != restaurantId)`. La règle était donc
    /// une CONSÉQUENCE d'un parcours, pas une propriété du panier — invisible en
    /// base, et fausse le jour où un chemin d'écriture oublierait le contrôle.
    ///
    /// Ici, changer de restaurant est littéralement impossible : la colonne est
    /// posée à la création et n'a pas de setter. Un panier ne mélange pas deux
    /// cuisines parce qu'il n'a qu'une colonne pour en désigner une.
    ///
    /// Pourquoi cette règle existe : deux établissements, ce sont deux temps de
    /// préparation et deux collectes. Le livreur devrait attendre le plus lent en
    /// laissant refroidir l'autre.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Guid RestaurantId { get; private set; }

    public string Currency { get; private set; } = default!;

    public FoodCartStatus Status { get; private set; }

    /// <summary>Code promo saisi par l'acheteur, ou null. Validé par Pricing, pas ici.</summary>
    public string? PromotionCode { get; private set; }

    public IReadOnlyCollection<FoodCartItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Ouvre un panier pour un acheteur, chez un établissement, dans une devise.
    /// </summary>
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

    /// <summary>
    /// Ajoute un plat, ou augmente la quantité de la ligne identique existante.
    ///
    /// LE MONTANT ARRIVE DE LA CARTE, PAS DU CLIENT.
    ///
    /// L'appelant l'a lu dans `IFoodModuleApi.GetMenuItemAsync` : prix de base du
    /// plat plus l'écart de chaque option retenue. C'est la différence de fond
    /// avec l'ancien chemin, où `unitBaseAmount` traversait le corps HTTP et où
    /// n'importe qui pouvait commander un plat à un franc.
    ///
    /// Ce que le panier garantit, lui : l'établissement, la devise, la quantité,
    /// et l'unicité de la combinaison plat + options.
    /// </summary>
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
        //
        // `MenuItem.PriceSelection` refuse déjà un total négatif, et la lecture de
        // carte est le seul chemin qui alimente ce montant. Mais cette méthode est
        // publique : le second appelant qui l'utilisera sans passer par la carte
        // n'aura pas cette protection, et un panier à montant négatif se paie en
        // crédit.
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

    /// <summary>
    /// Modifie la quantité d'une ligne désignée par SON identifiant.
    ///
    /// PAR LA LIGNE, ET JAMAIS PAR LE PLAT.
    ///
    /// Le même « riz au gras » peut figurer deux fois, une fois avec du poulet et
    /// une fois sans. Seul l'identifiant de ligne les distingue — c'est pourquoi
    /// il n'existe pas ici d'équivalent des routes `/items/{offerId}` de la
    /// marketplace, qui n'auraient pas su laquelle des deux viser.
    /// </summary>
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
    /// d'une promotion — sans quoi « bienvenue10 » ne trouverait jamais
    /// « BIENVENUE10 ».
    ///
    /// N'ATTESTE de rien : ni que le code existe, ni qu'il est actif, ni qu'il
    /// est applicable. Seul Pricing détient les promotions ; l'appelant valide
    /// AVANT d'appeler.
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
