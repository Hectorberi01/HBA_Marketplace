using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Domain.Carts.Events;

namespace HBA.Commerce.Domain.Carts;

/// <summary>
/// Panier d'un acheteur. Garde un snapshot des lignes (offre + prix de base) ;
/// le prix effectif avec promotions est calculé à la volée par Pricing. Une
/// seule devise par panier. Agrégat racine : possède ses lignes.
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

    /// <summary>
    /// Code promo saisi par l'acheteur, ou null.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// CE CHAMP MANQUAIT, ET C'EST POURQUOI AUCUN COUPON N'A JAMAIS FONCTIONNÉ.
    ///
    /// `CartPricer` passait `Code: null` EN DUR à Pricing. Or `Promotion.AppliesTo()`
    /// rejette toute promotion qui exige un code si le code fourni ne correspond pas —
    /// et `null` ne correspond jamais. Toute promotion codée était donc, littéralement,
    /// inapplicable. Le back-office pouvait en créer, l'admin pouvait les activer, et
    /// aucun acheteur au monde ne pouvait en bénéficier : il n'existait aucun chemin
    /// pour transmettre un code depuis le front.
    ///
    /// Le panier est le bon porteur : le code s'applique au panier entier, survit à la
    /// navigation, et sera figé dans la commande au checkout.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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
    /// La nature des lignes déjà présentes, ou <c>null</c> si le panier est vide.
    ///
    /// UN PANIER NE MÉLANGE PAS PLATS ET MARCHANDISE.
    ///
    /// Ce n'est pas une préférence d'affichage : une commande mixte devrait être à
    /// la fois préparée en cuisine et expédiée d'un entrepôt, réserver du stock
    /// pour une moitié seulement, et produire deux livraisons dont l'une doit
    /// arriver chaude en trente minutes et l'autre sous trois jours. Le refuser à
    /// l'entrée du panier est le seul endroit où l'on peut encore l'expliquer au
    /// client ; plus tard, c'est une commande payée qu'on ne sait pas honorer.
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
        //
        // Une ligne food a `OfferId == Guid.Empty`. Sans ce filtre, ajouter une
        // offre dont l'identifiant serait vide — ou simplement se tromper d'appel —
        // ferait tomber sur le premier plat du panier et en augmenterait la
        // quantité. Le panier ne peut pas être mixte, mais le code ne doit pas
        // dépendre de cette garantie pour rester juste.
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

    /// <summary>
    /// Ajoute un plat au panier.
    ///
    /// LE PRIX EST UNE ESTIMATION, ET LES OPTIONS N'ONT PAS ÉTÉ VALIDÉES ICI.
    ///
    /// Le panier ne connaît pas la carte du restaurant : il ne sait pas si le plat
    /// est disponible, si un groupe obligatoire a bien reçu son choix, ni si une
    /// option appartient réellement à ce plat. C'est l'appelant — la couche qui
    /// voit Cart et Food — qui l'a vérifié, et c'est Food qui refera le calcul du
    /// prix au moment de recevoir la commande.
    ///
    /// Ce que le panier garantit, lui : la devise, la quantité, l'unicité de la
    /// combinaison plat + options, et l'homogénéité du panier.
    /// </summary>
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
        //
        // `MenuItem.PriceSelection` refuse déjà un total négatif, et c'est le seul
        // chemin qui alimente ce montant. Mais `AddFoodItem` est publique : le
        // second appelant qui l'utilisera sans passer par la cotation n'aura pas
        // cette protection, et un panier à montant négatif se paie en crédit.
        if (unitBaseAmount < 0m)
        {
            return Result.Failure(Error.Validation(
                "cart.food.price_invalid", "Le prix d'un plat ne peut pas être négatif."));
        }

        // UN PANIER FOOD NE PORTE QU'UN SEUL RESTAURANT.
        //
        // Deux établissements, ce sont deux cuisines, deux temps de préparation et
        // deux collectes : le livreur devrait attendre le plus lent en laissant
        // refroidir l'autre. Aucun service de livraison de repas ne le permet, et
        // le domaine doit le dire plutôt que de laisser l'exploitation le
        // découvrir.
        // Le filtre sur la nature est redondant AUJOURD'HUI — `VerifierAjout` a
        // déjà écarté les paniers de marchandise. Il est là pour la même raison
        // que son jumeau dans `AddItem` : le jour où l'homogénéité serait
        // assouplie, ce prédicat se mettrait à refuser tout ajout de plat sur un
        // panier de marchandise, avec un message parlant d'un « autre restaurant »
        // qui n'existe pas. Un code juste par accident finit par cesser de l'être.
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
        //
        // Le champ s'appelait `OfferId`, et publier l'identifiant d'un plat sous ce
        // nom aurait fait chercher une offre inexistante à tout consommateur futur.
        // Il est renommé `ItemId` et accompagné de la nature : aucun handler ne
        // l'écoute aujourd'hui, c'est donc le moment où le renommage est gratuit.
        Raise(new ItemAddedToCartDomainEvent(Id.Value, menuItemId, quantity, CartLineKind.Food.ToString()));
        return Result.Success();
    }

    /// <summary>
    /// Les contrôles communs à tout ajout : panier modifiable, quantité positive,
    /// devise du panier, et homogénéité des natures.
    ///
    /// FACTORISÉ PARCE QUE L'HOMOGÉNÉITÉ EST LA SEULE RÈGLE QUI SE PERD.
    ///
    /// Les trois premiers contrôles existaient déjà et se recopient sans risque.
    /// Le quatrième est nouveau : écrit deux fois, il aurait fini par ne protéger
    /// qu'un seul des deux points d'entrée, et le panier mixte serait revenu par
    /// celui qu'on aurait oublié.
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

    /// <summary>
    /// Modifie la quantité d'une ligne désignée par SON identifiant.
    ///
    /// INDISPENSABLE POUR LES PLATS, ET PLUS SÛR POUR TOUT LE RESTE.
    ///
    /// Une ligne food ne se désigne pas par son plat : le même « riz au gras »
    /// peut figurer deux fois, une fois avec du poulet et une fois sans. Seul
    /// l'identifiant de ligne les distingue. Les routes marchandise continuent de
    /// passer par l'offre, par compatibilité — mais toute nouvelle surface devrait
    /// utiliser celle-ci.
    /// </summary>
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

    /// <summary>
    /// Applique un code promo au panier. Le code est normalisé (trim + MAJUSCULES), comme
    /// à la création d'une promotion — sans quoi « bienvenue10 » ne trouverait jamais
    /// « BIENVENUE10 ».
    ///
    /// Cette méthode n'ATTESTE de rien : elle ne dit pas que le code existe, qu'il est
    /// actif, ni qu'il est applicable. La validation est faite par le module Pricing, seul
    /// détenteur des promotions — le panier ne doit pas savoir ce qu'est une promotion.
    /// L'appelant (ApplyCouponCommandHandler) valide AVANT d'appeler.
    /// </summary>
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
