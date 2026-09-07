using HBA.Catalog.Domain.Offers.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Offers;

public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.NewGuid());
}

/// <summary>L'OFFRE COMMERCIALE D'UN VENDEUR SUR UNE VARIANTE.</summary>
public sealed class ProductOffer : AggregateRoot<OfferId>
{
    // ctor EF.
    private ProductOffer()
    {
    }

    private ProductOffer(
        OfferId id,
        Guid productId,
        Guid variantId,
        Guid storeId,
        Guid sellerId,
        Money sellerPrice,
        OfferCondition condition,
        FulfillmentType fulfillment,
        Guid shipFromLocationId,
        int handlingTimeDays)
        : base(id)
    {
        ProductId = productId;
        VariantId = variantId;
        StoreId = storeId;
        SellerId = sellerId;
        SellerPrice = sellerPrice;
        Condition = condition;
        Fulfillment = fulfillment;
        ShipFromLocationId = shipFromLocationId;
        HandlingTimeDays = handlingTimeDays;

        // NAÎT EN BROUILLON, PAS EN VENTE.
        Status = OfferStatus.Draft;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid ProductId { get; private set; }

    /// <summary>La variante vendue. Obligatoire — voir l'encadré de l'agrégat.</summary>
    public Guid VariantId { get; private set; }

    /// <summary>La boutique qui vend. Le cahier l'appelle StoreId.</summary>
    public Guid StoreId { get; private set; }

    /// <summary>Le compte vendeur derrière la boutique, pour les contrôles de propriété.</summary>
    public Guid SellerId { get; private set; }

    /// <summary>Ce que le vendeur touche, hors commission et frais prestataire.</summary>
    public Money SellerPrice { get; private set; } = default!;

    /// <summary>Part plateforme, figée au dernier calcul de prix.</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>Frais du prestataire de paiement, figés au dernier calcul.</summary>
    public decimal ProviderFeeAmount { get; private set; }

    /// <summary>Prix payé par l'acheteur : prix vendeur + commission + frais.</summary>
    public Money BuyerPrice { get; private set; } = default!;

    /// <summary>Prix promotionnel EN VIGUEUR, exprimé côté acheteur.</summary>
    public Money? PromotionalPrice { get; private set; }

    /// <summary>Fin de la remise. Nulle = sans échéance.</summary>
    public DateTime? PromotionEndsOnUtc { get; private set; }

    public OfferCondition Condition { get; private set; }
    public FulfillmentType Fulfillment { get; private set; }
    public Guid ShipFromLocationId { get; private set; }
    public int HandlingTimeDays { get; private set; }

    public OfferStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? UpdatedOnUtc { get; private set; }

    /// <summary>Prix réellement demandé à l'acheteur aujourd'hui.</summary>
    public Money EffectivePrice => IsPromotionRunning(DateTime.UtcNow) ? PromotionalPrice! : BuyerPrice;

    public bool IsPromotionRunning(DateTime nowUtc)
        => PromotionalPrice is not null
           && (PromotionEndsOnUtc is null || PromotionEndsOnUtc > nowUtc);

    public static Result<ProductOffer> Create(
        Guid productId,
        Guid variantId,
        Guid storeId,
        Guid sellerId,
        decimal sellerPrice,
        string currency,
        OfferCondition condition,
        FulfillmentType fulfillment,
        Guid shipFromLocationId,
        int handlingTimeDays,
        OfferPricingRates rates)
    {
        if (productId == Guid.Empty)
        {
            return Error.Validation("products.offer.product_required", "Une offre porte sur un produit.");
        }

        if (variantId == Guid.Empty)
        {
            return Error.Validation(
                "products.offer.variant_required",
                "Une offre porte sur une variante précise du produit.");
        }

        if (storeId == Guid.Empty)
        {
            return Error.Validation("products.offer.store_required", "Une offre appartient à une boutique.");
        }

        if (handlingTimeDays is < 0 or > 30)
        {
            return Error.Validation(
                "products.offer.handling_time_invalid",
                "Le délai de préparation doit être compris entre 0 et 30 jours.");
        }

        var prix = Money.Create(sellerPrice, currency);
        if (prix.IsFailure)
        {
            return Result.Failure<ProductOffer>(prix.Error);
        }

        var offer = new ProductOffer(
            OfferId.New(), productId, variantId, storeId, sellerId, prix.Value,
            condition, fulfillment, shipFromLocationId, handlingTimeDays);

        var breakdown = offer.ComputeBuyerPrice(rates);
        if (breakdown.IsFailure)
        {
            return Result.Failure<ProductOffer>(breakdown.Error);
        }

        offer.Raise(new ProductOfferCreatedDomainEvent(
            offer.Id.Value, productId, variantId, storeId, sellerId, offer.BuyerPrice.Amount, offer.BuyerPrice.Currency));

        return offer;
    }

    /// <summary>Change le prix vendeur. Le prix acheteur est recalculé, jamais saisi.</summary>
    public Result ChangeSellerPrice(decimal newSellerPrice, OfferPricingRates rates)
    {
        var prix = Money.Create(newSellerPrice, SellerPrice.Currency);
        if (prix.IsFailure)
        {
            return Result.Failure(prix.Error);
        }

        SellerPrice = prix.Value;

        var breakdown = ComputeBuyerPrice(rates);
        if (breakdown.IsFailure)
        {
            return breakdown;
        }

        // UNE REMISE NE SURVIT PAS À UN CHANGEMENT DE PRIX.
        PromotionalPrice = null;
        PromotionEndsOnUtc = null;

        UpdatedOnUtc = DateTime.UtcNow;
        Raise(new ProductOfferPriceChangedDomainEvent(Id.Value, ProductId, BuyerPrice.Amount, BuyerPrice.Currency));

        return Result.Success();
    }

    /// <summary>Applique une remise, exprimée en prix NET VENDEUR promotionnel.</summary>
    /// <param name="promotionalSellerPrice">Le net vendeur pendant la promotion.</param>
    public Result ApplyPromotion(
        decimal promotionalSellerPrice, DateTime? endsOnUtc, DateTime nowUtc, OfferPricingRates rates)
    {
        if (promotionalSellerPrice >= SellerPrice.Amount)
        {
            // Comparé sur le NET, pas sur le brut : c'est le seul montant que le
            // vendeur a saisi, donc le seul dont le refus lui soit intelligible.
            return Result.Failure(Error.Validation(
                "products.offer.promotion_not_lower",
                "Le prix promotionnel doit être inférieur au prix courant."));
        }

        var prix = Money.Create(
            BuyerPriceFor(promotionalSellerPrice, rates), BuyerPrice.Currency);
        if (prix.IsFailure)
        {
            return Result.Failure(prix.Error);
        }

        if (endsOnUtc is { } fin && fin <= nowUtc)
        {
            // Une remise déjà expirée à la pose n'est pas une remise : c'est un
            // affichage barré que personne ne peut obtenir.
            return Result.Failure(Error.Validation(
                "products.offer.promotion_already_over",
                "La fin de la promotion doit être dans le futur."));
        }

        PromotionalPrice = prix.Value;
        PromotionEndsOnUtc = endsOnUtc;
        UpdatedOnUtc = nowUtc;

        return Result.Success();
    }

    /// <summary>Change le délai de préparation annoncé à l'acheteur.</summary>
    public Result ChangeHandlingTime(int handlingTimeDays)
    {
        if (handlingTimeDays is < 0 or > 30)
        {
            return Result.Failure(Error.Validation(
                "products.offer.handling_time_invalid",
                "Le délai de préparation doit être compris entre 0 et 30 jours."));
        }

        HandlingTimeDays = handlingTimeDays;
        UpdatedOnUtc = DateTime.UtcNow;

        return Result.Success();
    }

    public Result RemovePromotion()
    {
        PromotionalPrice = null;
        PromotionEndsOnUtc = null;
        UpdatedOnUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Met l'offre en vente.</summary>
    public Result Activate() => ChangeStatus(OfferStatus.Active, reason: null);

    public Result Pause() => ChangeStatus(OfferStatus.Paused, reason: null);

    /// <summary>
    /// Déclare la rupture. Appelé par Inventory, pas par le vendeur : c'est le
    /// stock qui décide, et lui seul sait.
    /// </summary>
    public Result MarkOutOfStock() => ChangeStatus(OfferStatus.OutOfStock, reason: null);

    /// <summary>Retrait par la plateforme, avec motif.</summary>
    public Result Suspend(string? reason) => ChangeStatus(OfferStatus.Suspended, reason);

    public Result Archive() => ChangeStatus(OfferStatus.Archived, reason: null);

    private Result ChangeStatus(OfferStatus target, string? reason)
    {
        if (Status == target)
        {
            // Idempotent : rejouer une commande ne doit pas produire une erreur que
            // l'appelant traiterait comme un échec.
            return Result.Success();
        }

        if (!OfferStatusTransitions.IsAllowed(Status, target))
        {
            return Result.Failure(OfferStatusTransitions.CannotTransition(Status, target));
        }

        var previous = Status;
        Status = target;
        StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedOnUtc = DateTime.UtcNow;

        Raise(new ProductOfferStatusChangedDomainEvent(
            Id.Value, ProductId, previous.ToString(), target.ToString()));

        return Result.Success();
    }

    /// <summary>Empile commission et frais prestataire sur le prix vendeur.</summary>
    /// <summary>Le prix acheteur correspondant à un net vendeur donné.</summary>
    private static decimal BuyerPriceFor(decimal sellerAmount, OfferPricingRates rates)
        => sellerAmount
         + Math.Round(sellerAmount * rates.CommissionRate, 0, MidpointRounding.AwayFromZero)
         + Math.Round(sellerAmount * rates.ProviderFeeRate, 0, MidpointRounding.AwayFromZero);

    private Result ComputeBuyerPrice(OfferPricingRates rates)
    {
        var commission = Math.Round(SellerPrice.Amount * rates.CommissionRate, 0, MidpointRounding.AwayFromZero);
        var frais = Math.Round(SellerPrice.Amount * rates.ProviderFeeRate, 0, MidpointRounding.AwayFromZero);

        var acheteur = Money.Create(SellerPrice.Amount + commission + frais, SellerPrice.Currency);
        if (acheteur.IsFailure)
        {
            return Result.Failure(acheteur.Error);
        }

        CommissionAmount = commission;
        ProviderFeeAmount = frais;
        BuyerPrice = acheteur.Value;

        return Result.Success();
    }
}

/// <summary>Barème appliqué au prix vendeur.</summary>
public readonly record struct OfferPricingRates(decimal CommissionRate, decimal ProviderFeeRate);

/// <summary>État du bien vendu.</summary>
public enum OfferCondition
{
    New = 0,
    Used = 1,
    Refurbished = 2
}

/// <summary>Qui expédie : le vendeur (FBS) ou la plateforme (FBP).</summary>
public enum FulfillmentType
{
    Fbs = 0,

    /// <summary>INATTEIGNABLE, ET COHÉRENT AVEC LE RESTE (lot 9.2).</summary>
    Fbp = 1
}
