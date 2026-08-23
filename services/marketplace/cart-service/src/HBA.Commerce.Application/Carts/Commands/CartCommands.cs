using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Commerce.Application.Abstractions;
using HBA.Commerce.Domain.Carts;
using HBA.Pricing.Contracts;

namespace HBA.Commerce.Application.Carts.Commands;

/// <summary>Change la quantité d'une ligne (0 ou moins la retire).</summary>
public sealed record UpdateCartItemQuantityCommand(Guid BuyerId, Guid OfferId, int Quantity) : ICommand;

/// <summary>Retire une ligne du panier.</summary>
public sealed record RemoveCartItemCommand(Guid BuyerId, Guid OfferId) : ICommand;

/// <summary>
/// Modifie la quantité d'une ligne désignée par SON identifiant.
///
/// INDISPENSABLE DEPUIS QUE LE PANIER PORTE DES PLATS.
///
/// `UpdateCartItemQuantityCommand` désigne la ligne par son OFFRE. Une ligne food
/// n'en a pas, et le même plat peut y figurer deux fois avec des options
/// différentes : seul l'identifiant de ligne les distingue.
/// </summary>
public sealed record UpdateCartLineQuantityCommand(Guid BuyerId, Guid LineId, int Quantity) : ICommand;

/// <summary>Retire une ligne désignée par son identifiant.</summary>
public sealed record RemoveCartLineCommand(Guid BuyerId, Guid LineId) : ICommand;

/// <summary>Vide le panier actif.</summary>
public sealed record ClearCartCommand(Guid BuyerId) : ICommand;

// `CheckoutCartCommand` A ÉTÉ RETIRÉE AVEC SA ROUTE.
//
// Son résumé annonçait « déclenche la création de commande côté Ordering ». Elle
// ne déclenchait rien : elle marquait le panier `CheckedOut` et rendait
// `cart.Id.Value` — que la route présentait comme un identifiant de commande. En
// clôturant le panier, elle faisait échouer le `POST /api/orders` qui aurait dû
// suivre, sur `ordering.cart_empty`.
//
// Le résumé décrivait l'intention, le corps faisait autre chose, et les deux ont
// cohabité parce que rien ne les confrontait. Voir l'encadré de
// `CommerceEndpoints`.

/// <summary>Applique un code promo au panier.</summary>
public sealed record ApplyCouponCommand(Guid BuyerId, string Code) : ICommand;

/// <summary>Retire le code promo du panier.</summary>
public sealed record RemoveCouponCommand(Guid BuyerId) : ICommand;

internal abstract class CartCommandHandlerBase
{
    protected readonly ICartRepository CartRepository;
    protected readonly ICartUnitOfWork UnitOfWork;
    private readonly ICacheService _cache;

    protected CartCommandHandlerBase(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
    {
        CartRepository = cartRepository;
        UnitOfWork = unitOfWork;
        _cache = cache;
    }

    /// <summary>Persiste puis invalide le cache du panier de l'acheteur (read-your-writes).</summary>
    protected async Task SaveAndInvalidateAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(buyerId), cancellationToken);
    }
}

internal sealed class UpdateCartItemQuantityCommandHandler
    : CartCommandHandlerBase, ICommandHandler<UpdateCartItemQuantityCommand>
{
    public UpdateCartItemQuantityCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(UpdateCartItemQuantityCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.UpdateItemQuantity(command.OfferId, command.Quantity);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCartItemCommandHandler
    : CartCommandHandlerBase, ICommandHandler<RemoveCartItemCommand>
{
    public RemoveCartItemCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(RemoveCartItemCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.RemoveItem(command.OfferId);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class UpdateCartLineQuantityCommandHandler
    : CartCommandHandlerBase, ICommandHandler<UpdateCartLineQuantityCommand>
{
    public UpdateCartLineQuantityCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(UpdateCartLineQuantityCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        // LA LIGNE EST CHERCHÉE DANS LE PANIER DE L'ACHETEUR DU JETON.
        //
        // C'est ce qui rend l'identifiant de ligne sûr à exposer : il ne désigne
        // rien en dehors du panier qu'on vient de charger pour CE compte.
        var result = cart.UpdateLineQuantity(command.LineId, command.Quantity);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCartLineCommandHandler
    : CartCommandHandlerBase, ICommandHandler<RemoveCartLineCommand>
{
    public RemoveCartLineCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(RemoveCartLineCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.RemoveLine(command.LineId);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class ClearCartCommandHandler
    : CartCommandHandlerBase, ICommandHandler<ClearCartCommand>
{
    public ClearCartCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(ClearCartCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        cart.Clear();
        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Attache un code promo au panier — après l'avoir fait valider par Pricing.
///
/// La validation est INDICATIVE : elle sert à répondre tout de suite « code expiré » ou
/// « déjà utilisé », plutôt que de laisser l'acheteur porter un code mort jusqu'au
/// checkout et découvrir que la remise ne s'applique pas. Elle n'engage rien : le coupon
/// n'est réellement consommé qu'à la CONFIRMATION de la commande, sous verrou
/// (RedeemPromotionOnOrderConfirmedHandler).
///
/// Le panier, lui, ne sait pas ce qu'est une promotion — et ne doit pas le savoir. Il
/// stocke une chaîne ; Pricing en fait le sens.
/// </summary>
internal sealed class ApplyCouponCommandHandler
    : CartCommandHandlerBase, ICommandHandler<ApplyCouponCommand>
{
    private readonly IPricingModuleApi _pricing;

    public ApplyCouponCommandHandler(
        ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache, IPricingModuleApi pricing)
        : base(cartRepository, unitOfWork, cache) => _pricing = pricing;

    public async Task<Result> Handle(ApplyCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        // LE SOUS-TOTAL EST TRANSMIS DEPUIS LE LOT D28, ET IL CHANGE LE VERDICT.
        //
        // Une campagne peut porter une condition « panier d'au moins 5 000 F ».
        // Sans le sous-total, Pricing ne pouvait pas l'évaluer : il répondait
        // « code valide », l'acheteur l'attachait, et découvrait au calcul du
        // panier qu'aucune remise ne s'appliquait — le parcours exact que cette
        // validation existe pour éviter.
        //
        // Le montant est le même que celui que `CartPricer` additionne : les prix
        // effectifs ne sont pas encore connus ici, et c'est bien la BASE que la
        // condition de montant regarde.
        var sousTotal = cart.Items.Sum(i => i.UnitBaseAmount * i.Quantity);

        var validation = await _pricing.ValidateCouponAsync(
            command.Code, command.BuyerId, sousTotal, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(Error.Validation(
                validation.ErrorCode ?? "pricing.coupon.invalid",
                validation.ErrorMessage ?? "Ce code promo est invalide."));
        }

        var result = cart.ApplyPromotionCode(command.Code);
        if (result.IsFailure)
        {
            return result;
        }

        // Invalide le panier valorisé en cache : sans cela, l'acheteur appliquerait son
        // code et verrait pendant 2 minutes l'ANCIEN total, sans remise. Il croirait le
        // code refusé et le ressaisirait — ou abandonnerait.
        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCouponCommandHandler
    : CartCommandHandlerBase, ICommandHandler<RemoveCouponCommand>
{
    public RemoveCouponCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(RemoveCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.RemovePromotionCode();
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}
