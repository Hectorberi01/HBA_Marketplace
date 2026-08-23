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

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// AJOUT D'UNE LIGNE AU PANIER.
///
/// Résout l'offre et son produit via <c>IProductsModuleApi</c>, vérifie le stock
/// via Inventory, puis ajoute la ligne au panier actif — créé à la volée dans la
/// devise de l'offre s'il n'existe pas.
///
/// PREMIER APPELANT BASCULÉ DU MODULE OFFERS VERS PRODUCTS.
///
/// Ce handler lisait <c>IOfferModuleApi</c> pour l'offre et <c>ICatalogModuleApi</c>
/// pour la catégorie du produit. Les deux vivent désormais dans le même module :
/// une dépendance disparaît, et surtout une INCOHÉRENCE devient impossible —
/// l'offre et le produit ne peuvent plus venir de deux sources qui divergent.
///
/// Le module Offers n'est PAS encore supprimé : Search, Notifications et les BFF
/// le lisent toujours. La bascule se fait appelant par appelant, et l'ancienne
/// donnée reste en place jusqu'à ce que le dernier ait migré.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
        //
        // Le module Products/Offers n'est pas extrait : aucun service HBA ne
        // détient les offres, et le client gRPC lève désormais plutôt que de
        // rendre `null`. Le rattraper ici transforme une panne d'infrastructure
        // en refus métier LISIBLE — « le catalogue n'est pas disponible » — au
        // lieu d'un « offre introuvable » qui ferait chercher un produit
        // manquant.
        //
        // La distinction compte : l'un se corrige en republiant une offre,
        // l'autre en extrayant un module.
        OfferSummary? offer;

        try
        {
            offer = await _products.GetOfferAsync(command.OfferId, cancellationToken);
        }
        catch (NotSupportedException)
        {
            // `Error.Failure` et non `NotFound` : la nuance est le fond du sujet.
            // « Introuvable » ferait chercher une offre à republier ; « panne »
            // dit qu'aucune offre ne peut être lue, quelle qu'elle soit.
            return Result.Failure<Guid>(Error.Failure(
                "cart.catalog_unavailable",
                "Le catalogue des offres n'est pas disponible sur cette installation."));
        }

        if (offer is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.offer.not_found", "Offre introuvable."));
        }

        // ON LIT UN DRAPEAU, PLUS UNE CHAÎNE.
        //
        // L'ancien code comparait `offer.Status` à « Active ». C'est le panier
        // qui décidait alors quels statuts sont achetables — une règle recopiée
        // hors du module qui la détient, et qui serait devenue silencieusement
        // fausse en passant de trois statuts à six : « Suspended » et « Draft »
        // n'existaient pas, et la comparaison les aurait simplement refusés sans
        // que personne ne vérifie que c'était voulu.
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
        //
        // Rien ne propage encore la suspension d'un produit vers ses offres. Un
        // produit retiré par la plateforme — contrefaçon signalée, litige —
        // gardait donc des offres actives, et restait ajoutable au panier par
        // quiconque avait gardé l'onglet ouvert ou le lien.
        if (!product.IsVisible)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "cart.product.not_available", "Ce produit n'est plus proposé à la vente."));
        }

        // Le SKU rattache la ligne au stock. Il est facultatif sur une variante,
        // mais la création d'offre l'exige : une offre sans SKU ne devrait pas
        // exister. On refuse explicitement plutôt que de déréférencer — le jour
        // où une telle offre apparaîtrait, le message dit quoi corriger.
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

        // EffectivePrice et non BuyerPrice : c'est le prix promotionnel s'il
        // court, le prix courant sinon. L'ancien contrat servait déjà le prix
        // remisé dans « BasePriceAmount » ; prendre ici « BuyerPrice » ferait
        // payer le plein tarif un article affiché en promotion.
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
