using HBA.DeliveryPricing.Contracts;
using HBA.Food.Contracts;
using HBA.FoodCarts.Contracts;
using HBA.FoodOrders.Application.Abstractions;
using HBA.FoodOrders.Domain.Orders;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using OrderAggregate = HBA.FoodOrders.Domain.Orders.MealOrder;

namespace HBA.FoodOrders.Application.Orders.Commands;

/// <summary>Adresse de livraison choisie au paiement (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);

/// <summary>Passe la commande à partir du panier de repas actif.</summary>
/// <param name="DeliveryQuoteId">Le devis de course qui FIXE les frais de livraison.</param>
public sealed record PlaceMealOrderCommand(
    Guid BuyerId,
    ShippingAddressInput? ShippingAddress = null,
    string? DeliveryQuoteId = null,
    string? CustomerNote = null) : ICommand<Guid>;

internal sealed class PlaceMealOrderCommandHandler : ICommandHandler<PlaceMealOrderCommand, Guid>
{
    /// <summary>
    /// Tolérance de comparaison entre le point de chute FIGÉ dans le devis et celui
    /// de l'adresse enregistrée sur la commande.
    /// </summary>
    private const double ToleranceDegres = 0.0005;

    /// <summary>UN REPAS PART EN « EXPRESS », ET LE DEVIS DOIT L'AVOIR ÉTÉ AUSSI.</summary>
    private const string NiveauDeService = "Express";

    private readonly IFoodCartModuleApi _carts;
    private readonly IFoodModuleApi _food;
    /// <summary>La relecture du devis de course.</summary>
    private readonly IDeliveryQuoteLookup _devis;
    private readonly IMealOrderRepository _orders;
    private readonly IMealOrderUnitOfWork _unitOfWork;

    public PlaceMealOrderCommandHandler(
        IFoodCartModuleApi carts,
        IFoodModuleApi food,
        IDeliveryQuoteLookup devis,
        IMealOrderRepository orders,
        IMealOrderUnitOfWork unitOfWork)
    {
        _carts = carts;
        _food = food;
        _devis = devis;
        _orders = orders;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(PlaceMealOrderCommand command, CancellationToken cancellationToken)
    {
        var panier = await _carts.GetActiveCartAsync(command.BuyerId, cancellationToken);
        if (panier is null || panier.Lines.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.cart_empty", "Aucun panier de repas à commander."));
        }

        if (panier.RestaurantId is not { } restaurantId || restaurantId == Guid.Empty)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.cart_without_restaurant", "Ce panier ne désigne aucun restaurant."));
        }

        // IDEMPOTENCE : LE MÊME PANIER NE PRODUIT QU'UNE COMMANDE.
        var deja = await _orders.GetByCartAsync(panier.CartId, cancellationToken);
        if (deja is not null)
        {
            return Result.Success(deja.Id.Value);
        }

        // LE RESTAURANT DOIT ENCORE PRENDRE DES COMMANDES AU MOMENT DE PAYER.
        var restaurant = await _food.GetRestaurantAsync(restaurantId, cancellationToken);
        if (restaurant is null || !restaurant.IsPubliclyVisible)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "food_ordering.restaurant_not_found", "Établissement introuvable."));
        }

        if (!restaurant.AcceptsOrdersNow)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_ordering.restaurant_closed",
                "Ce restaurant ne prend pas de commande en ce moment."));
        }

        // LE MINIMUM DE COMMANDE SE VÉRIFIE ICI AUSSI, PAS SEULEMENT EN CUISINE.
        if (restaurant.MinimumOrderAmount is { } minimum && panier.GrandTotal < minimum)
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.below_minimum",
                $"Ce restaurant demande une commande d'au moins {minimum:0.##} {panier.Currency}."));
        }

        var brouillons = panier.Lines.Select(l => new MealOrderLineDraft(
            l.MenuItemId,
            l.Name,
            l.Quantity,
            l.UnitBaseAmount,
            l.SellerDiscount,
            l.PlatformDiscount,
            l.FinalUnitPrice,
            l.Notes,
            l.Options.Select(o => new MealOrderLineOptionDraft(o.OptionGroupId, o.OptionId)).ToList()));

        var creation = OrderAggregate.Create(
            command.BuyerId,
            restaurantId,
            panier.CartId,
            panier.Currency,
            brouillons,
            panier.PromotionCode,
            command.CustomerNote);

        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        var commande = creation.Value;

        // L'ADRESSE EST UN INVARIANT DE LA COMMANDE, PAS UNE VÉRIFICATION
        // D'INTERFACE.
        if (command.ShippingAddress is not { } adresse
            || string.IsNullOrWhiteSpace(adresse.CommuneCode)
            || string.IsNullOrWhiteSpace(adresse.Landmark))
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.shipping_address_required",
                "Une adresse de livraison complète est obligatoire : commune et point de repère."));
        }

        if (adresse.Latitude is null
            || adresse.Longitude is null
            || string.IsNullOrWhiteSpace(adresse.Phone))
        {
            return Result.Failure<Guid>(Error.Validation(
                "food_ordering.address_incomplete",
                "Une commande de repas exige une position sur la carte et un téléphone joignable."));
        }

        commande.SetShippingAddress(
            adresse.Label, adresse.Recipient, adresse.Phone,
            adresse.CommuneCode, adresse.Quartier, adresse.Landmark, adresse.Line1, adresse.CountryCode,
            adresse.Latitude, adresse.Longitude);

        var frais = await ResoudreFraisAsync(adresse, command.DeliveryQuoteId, cancellationToken);
        if (frais.IsFailure)
        {
            return Result.Failure<Guid>(frais.Error);
        }

        commande.SetShippingFee(frais.Value.Montant, frais.Value.QuoteId);

        await _orders.AddAsync(commande, cancellationToken);

        // AUCUNE RÉSERVATION DE STOCK, ET IL N'Y A RIEN À RÉSERVER.
        commande.MarkAwaitingPayment();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return commande.Id.Value;
    }

    /// <summary>Les frais retenus, et le devis qui les fonde.</summary>
    private readonly record struct FraisDeCourse(decimal Montant, string QuoteId);

    /// <summary>DÉTERMINE LES FRAIS DE COURSE — SANS JAMAIS CROIRE LE CLIENT.</summary>
    private async Task<Result<FraisDeCourse>> ResoudreFraisAsync(
        ShippingAddressInput adresse, string? quoteId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(quoteId))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_required",
                "Les frais de livraison d'un repas doivent être chiffrés par un devis avant "
                + "le paiement. Demandez un devis de course, puis reprenez le paiement avec "
                + "son identifiant."));
        }

        var devis = await _devis.LookupQuoteAsync(quoteId, cancellationToken);

        // INTROUVABLE OU ILLISIBLE : MÊME REFUS, MÊME GESTE ATTENDU.
        if (devis is null)
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_not_found",
                "Ce devis de livraison est introuvable. Demandez un nouveau prix avant de payer."));
        }

        // CONSOMMÉ AVANT EXPIRÉ : L'ORDRE DES DEUX CONTRÔLES CHANGE LE MESSAGE.
        if (devis.IsConsumed)
        {
            return Result.Failure<FraisDeCourse>(Error.Conflict(
                "food_ordering.delivery_quote_used",
                "Ce devis de livraison a déjà servi à une course. Demandez un nouveau prix."));
        }

        if (devis.IsExpired)
        {
            return Result.Failure<FraisDeCourse>(Error.Conflict(
                "food_ordering.delivery_quote_expired",
                $"Ce devis de livraison a expiré le {devis.ExpiresAtUtc:u}. "
                + "Demandez un nouveau prix avant de payer."));
        }

        // UN DEVIS DE PARTENAIRE N'EST PAS UN DEVIS DE CLIENT.
        if (devis.PartnerId is not null)
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_foreign",
                "Ce devis de livraison n'a pas été établi pour cette commande."));
        }

        // LE DEVIS DOIT AVOIR ÉTÉ ÉTABLI POUR CETTE ADRESSE-CI.
        if (adresse.Latitude is { } lat && adresse.Longitude is { } lon
            && (Math.Abs(devis.DropoffLatitude - lat) > ToleranceDegres
                || Math.Abs(devis.DropoffLongitude - lon) > ToleranceDegres))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_address_mismatch",
                "Ce devis de livraison a été établi pour une autre adresse. "
                + "Demandez un nouveau prix pour l'adresse choisie."));
        }

        if (!string.Equals(devis.DeliveryType, NiveauDeService, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<FraisDeCourse>(Error.Validation(
                "food_ordering.delivery_quote_wrong_service",
                $"Ce devis de livraison a été établi pour un service « {devis.DeliveryType} », "
                + $"alors qu'un repas est livré en « {NiveauDeService} ». Demandez un nouveau prix."));
        }

        // C'EST LE MONTANT DU DEVIS QUI ENTRE DANS LA COMMANDE — PAS UN MONTANT
        // COMPARÉ AU DEVIS.
        return new FraisDeCourse(devis.Total, devis.QuoteId);
    }
}
