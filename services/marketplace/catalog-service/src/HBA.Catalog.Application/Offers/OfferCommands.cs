using FluentValidation;
using HBA.Catalog.Application.Abstractions;
using HBA.Catalog.Domain.Offers;
using HBA.Catalog.Domain.Products;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Offers;

/// <summary>Crée l'offre d'une boutique sur une variante.</summary>
public sealed record CreateOfferCommand(
    Guid ProductId,
    Guid VariantId,
    Guid StoreId,
    Guid SellerId,
    decimal SellerPrice,
    string Currency,
    string Condition,
    string FulfillmentType,
    Guid ShipFromLocationId,
    int HandlingTimeDays) : ICommand<Guid>;

public sealed record ChangeOfferPriceCommand(Guid OfferId, decimal SellerPrice) : ICommand;

/// <param name="PromotionalSellerPrice">Le NET VENDEUR pendant la promotion.</param>
public sealed record ApplyOfferPromotionCommand(
    Guid OfferId, decimal PromotionalSellerPrice, DateTime? EndsOnUtc) : ICommand;

public sealed record RemoveOfferPromotionCommand(Guid OfferId) : ICommand;

/// <summary>Change le délai de préparation annoncé à l'acheteur.</summary>
public sealed record SetOfferHandlingTimeCommand(Guid OfferId, int HandlingTimeDays) : ICommand;

public sealed record ActivateOfferCommand(Guid OfferId) : ICommand;

public sealed record PauseOfferCommand(Guid OfferId) : ICommand;

/// <summary>Rupture de stock. Émise par Inventory, pas par le vendeur.</summary>
public sealed record MarkOfferOutOfStockCommand(Guid OfferId) : ICommand;

public sealed record SuspendOfferCommand(Guid OfferId, string? Reason) : ICommand;

public sealed record ArchiveOfferCommand(Guid OfferId) : ICommand;

internal sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.VariantId).NotEmpty();
        RuleFor(c => c.StoreId).NotEmpty();
        RuleFor(c => c.SellerPrice).GreaterThan(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
        RuleFor(c => c.ShipFromLocationId).NotEmpty();
    }
}

internal sealed class OfferCommandHandler
    : ICommandHandler<CreateOfferCommand, Guid>,
      ICommandHandler<ChangeOfferPriceCommand>,
      ICommandHandler<ApplyOfferPromotionCommand>,
      ICommandHandler<RemoveOfferPromotionCommand>,
      ICommandHandler<SetOfferHandlingTimeCommand>,
      ICommandHandler<ActivateOfferCommand>,
      ICommandHandler<PauseOfferCommand>,
      ICommandHandler<MarkOfferOutOfStockCommand>,
      ICommandHandler<SuspendOfferCommand>,
      ICommandHandler<ArchiveOfferCommand>
{
    private readonly IProductRepository _products;
    private readonly IProductOfferRepository _offers;
    private readonly IOfferPricingSettings _pricing;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public OfferCommandHandler(
        IProductRepository products,
        IProductOfferRepository offers,
        IOfferPricingSettings pricing,
        ICatalogUnitOfWork unitOfWork)
    {
        _products = products;
        _offers = offers;
        _pricing = pricing;
        _unitOfWork = unitOfWork;
    }

    private OfferPricingRates Rates => new(_pricing.CommissionRate, _pricing.ProviderFeeRate);

    public async Task<Result<Guid>> Handle(CreateOfferCommand command, CancellationToken ct)
    {
        var product = await _products.GetByIdAsync(new ProductId(command.ProductId), ct);
        if (product is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "products.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        if (product.SellerId != command.SellerId)
        {
            // Le modèle retenu est « une fiche par vendeur » : une boutique ne peut
            // proposer que ses propres produits.
            return Result.Failure<Guid>(Error.Forbidden(
                "products.offer.not_your_product",
                "Vous ne pouvez créer une offre que sur vos propres fiches produit."));
        }

        // LA VARIANTE EST VÉRIFIÉE ICI. C'EST TOUT L'OBJET DU CORRECTIF.
        var variante = product.Variants.FirstOrDefault(v => v.Id == command.VariantId);
        if (variante is null)
        {
            return Result.Failure<Guid>(Error.Validation(
                "products.offer.variant_not_found",
                "Cette variante n'appartient pas au produit indiqué."));
        }

        // LA GARDE ANNONCÉE PAR L'ENCADRÉ CI-DESSUS EXISTE ENFIN (tâche #230).
        if (!variante.IsActive)
        {
            return Result.Failure<Guid>(Error.Validation(
                "products.offer.variant_inactive",
                "Cette déclinaison est retirée de la vente. Réactivez-la avant de la proposer."));
        }

        if (await _offers.ExistsForStoreAndVariantAsync(command.StoreId, command.VariantId, ct))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "products.offer.duplicate",
                "Cette boutique propose déjà une offre sur cette variante."));
        }

        if (!Enum.TryParse<OfferCondition>(command.Condition, ignoreCase: true, out var condition))
        {
            return Result.Failure<Guid>(Error.Validation(
                "products.offer.condition_invalid",
                $"État inconnu : « {command.Condition} ». Attendu : {string.Join(", ", Enum.GetNames<OfferCondition>())}."));
        }

        if (!Enum.TryParse<FulfillmentType>(command.FulfillmentType, ignoreCase: true, out var fulfillment))
        {
            return Result.Failure<Guid>(Error.Validation(
                "products.offer.fulfillment_invalid",
                $"Mode d'expédition inconnu : « {command.FulfillmentType} »."));
        }

        var offer = ProductOffer.Create(
            command.ProductId, command.VariantId, command.StoreId, command.SellerId,
            command.SellerPrice, command.Currency, condition, fulfillment,
            command.ShipFromLocationId, command.HandlingTimeDays, Rates);

        if (offer.IsFailure)
        {
            return Result.Failure<Guid>(offer.Error);
        }

        await _offers.AddAsync(offer.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return offer.Value.Id.Value;
    }

    public Task<Result> Handle(ChangeOfferPriceCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.ChangeSellerPrice(c.SellerPrice, Rates), ct);

    public Task<Result> Handle(ApplyOfferPromotionCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.ApplyPromotion(c.PromotionalSellerPrice, c.EndsOnUtc, DateTime.UtcNow, Rates), ct);

    public Task<Result> Handle(RemoveOfferPromotionCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.RemovePromotion(), ct);

    public Task<Result> Handle(SetOfferHandlingTimeCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.ChangeHandlingTime(c.HandlingTimeDays), ct);

    public Task<Result> Handle(ActivateOfferCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.Activate(), ct);

    public Task<Result> Handle(PauseOfferCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.Pause(), ct);

    public Task<Result> Handle(MarkOfferOutOfStockCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.MarkOutOfStock(), ct);

    public Task<Result> Handle(SuspendOfferCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.Suspend(c.Reason), ct);

    public Task<Result> Handle(ArchiveOfferCommand c, CancellationToken ct)
        => MutateAsync(c.OfferId, o => o.Archive(), ct);

    private async Task<Result> MutateAsync(Guid offerId, Func<ProductOffer, Result> decision, CancellationToken ct)
    {
        var offer = await _offers.GetByIdAsync(new OfferId(offerId), ct);
        if (offer is null)
        {
            return Result.Failure(Error.NotFound("products.offer.not_found", $"Offre {offerId} introuvable."));
        }

        var result = decision(offer);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
