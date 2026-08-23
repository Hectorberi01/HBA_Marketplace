using HBA.FoodCarts.Contracts;
using HBA.FoodCarts.Domain.Carts;
using HBA.FoodOrders.Contracts;
using HBA.Pricing.Contracts;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.FoodCarts.Application.Carts.Queries;

/// <summary>Le panier de repas actif de l'acheteur, valorisé.</summary>
public sealed record GetActiveFoodCartQuery(Guid BuyerId) : IQuery<FoodCartSummary>;

/// <summary>Un panier de repas valorisé, par son identifiant.</summary>
public sealed record GetFoodCartByIdQuery(Guid CartId) : IQuery<FoodCartSummary>;

internal sealed class GetActiveFoodCartQueryHandler : IQueryHandler<GetActiveFoodCartQuery, FoodCartSummary>
{
    private static readonly TimeSpan DureeDeCache = TimeSpan.FromMinutes(2);

    private readonly IFoodCartRepository _carts;
    private readonly IPricingModuleApi _pricing;
    private readonly IMealOrderModuleApi _orders;
    private readonly ICacheService _cache;

    public GetActiveFoodCartQueryHandler(
        IFoodCartRepository carts,
        IPricingModuleApi pricing,
        IMealOrderModuleApi orders,
        ICacheService cache)
    {
        _carts = carts;
        _pricing = pricing;
        _orders = orders;
        _cache = cache;
    }

    public async Task<Result<FoodCartSummary>> Handle(
        GetActiveFoodCartQuery query, CancellationToken cancellationToken)
    {
        // Cache-aside : le panier valorisé est une lecture chaude, invalidée à
        // chaque mutation (read-your-writes garanti côté commandes).
        var cle = FoodCartCacheKeys.Active(query.BuyerId);
        var enCache = await _cache.GetAsync<FoodCartSummary>(cle, cancellationToken);
        if (enCache is not null)
        {
            return enCache;
        }

        var cart = await _carts.GetActiveByBuyerAsync(query.BuyerId, cancellationToken);
        if (cart is null)
        {
            // UN PANIER VIDE, PAS UNE ERREUR — et sans restaurant.
            //
            // L'écran doit afficher « votre panier est vide », pas un échec. Le
            // restaurant est nul parce qu'aucun n'a encore été choisi : c'est le
            // premier ajout qui le fixe, et c'est ce qui rend le premier ajout
            // toujours possible.
            return new FoodCartSummary(
                Guid.Empty, query.BuyerId, null, "XOF", "Active", [], 0m, 0m, 0m, 0m);
        }

        var premiereCommande = !await _orders.HasPlacedOrderAsync(query.BuyerId, cancellationToken);
        var vue = await FoodCartPricer.PriceAsync(cart, _pricing, premiereCommande, cancellationToken);

        await _cache.SetAsync(cle, vue, DureeDeCache, cancellationToken);
        return vue;
    }
}

internal sealed class GetFoodCartByIdQueryHandler : IQueryHandler<GetFoodCartByIdQuery, FoodCartSummary>
{
    private readonly IFoodCartRepository _carts;
    private readonly IPricingModuleApi _pricing;
    private readonly IMealOrderModuleApi _orders;

    public GetFoodCartByIdQueryHandler(
        IFoodCartRepository carts, IPricingModuleApi pricing, IMealOrderModuleApi orders)
    {
        _carts = carts;
        _pricing = pricing;
        _orders = orders;
    }

    public async Task<Result<FoodCartSummary>> Handle(
        GetFoodCartByIdQuery query, CancellationToken cancellationToken)
    {
        var cart = await _carts.GetByIdAsync(new FoodCartId(query.CartId), cancellationToken);
        if (cart is null)
        {
            return Error.NotFound("food_cart.not_found", "Panier introuvable.");
        }

        // AUCUN CONTRÔLE DE PROPRIÉTAIRE ICI, ET C'EST VOULU.
        //
        // La même requête sert `FoodCartModuleApi`, c'est-à-dire l'appel gRPC de
        // food-order-service au moment de passer commande, où il n'y a pas
        // d'acheteur connecté à comparer. Le contrôle est posé dans la couche
        // HTTP, seule à voir un jeton. Le déplacer ici casserait le passage en
        // commande.
        var premiereCommande = !await _orders.HasPlacedOrderAsync(cart.BuyerId, cancellationToken);
        return await FoodCartPricer.PriceAsync(cart, _pricing, premiereCommande, cancellationToken);
    }
}
