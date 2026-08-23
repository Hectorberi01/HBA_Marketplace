using HBA.Food.Contracts;
using HBA.FoodCarts.Application.Abstractions;
using HBA.FoodCarts.Domain.Carts;
using HBA.Pricing.Contracts;
using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using CartAggregate = HBA.FoodCarts.Domain.Carts.FoodCart;

namespace HBA.FoodCarts.Application.Carts.Commands;

/// <summary>Une option retenue par le client : son groupe et son choix.</summary>
public sealed record FoodOptionChoice(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// AJOUT D'UN PLAT AU PANIER.
///
/// IL N'Y A PLUS DE PRIX DANS CETTE COMMANDE, ET C'EST LE POINT.
///
/// Son ancêtre — `AddFoodItemToCartCommand`, dans cart-service — recevait
/// `UnitBaseAmount` et `Currency` depuis le corps HTTP, et son propre
/// commentaire l'assumait : « ce gestionnaire n'interroge ni Food ni Pricing […]
/// tout cela a été vérifié par l'appelant ». L'appelant, c'était le navigateur du
/// client. Ni la disponibilité du plat, ni l'appartenance des options à ses
/// groupes, ni le prix n'étaient contrôlés nulle part sur ce chemin.
///
/// Ce n'était pas une négligence : cart-service vivait dans la marketplace et
/// n'avait aucun droit de connaître une carte de restaurant. La frontière était
/// juste, et c'est le PLACEMENT du panier qui était faux.
///
/// food-cart-service est dans le domaine restauration. Il lit la carte
/// (`IFoodModuleApi.GetMenuItemAsync`), vérifie et calcule. Le montant n'entre
/// plus par la porte du client.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record AddItemToFoodCartCommand(
    Guid BuyerId,
    Guid RestaurantId,
    Guid MenuItemId,
    int Quantity,
    string? Notes,
    IReadOnlyList<FoodOptionChoice> Options) : ICommand<Guid>;

internal sealed class AddItemToFoodCartCommandHandler : ICommandHandler<AddItemToFoodCartCommand, Guid>
{
    private readonly IFoodCartRepository _carts;
    private readonly IFoodCartUnitOfWork _unitOfWork;
    private readonly IFoodModuleApi _food;
    private readonly ICacheService _cache;

    public AddItemToFoodCartCommandHandler(
        IFoodCartRepository carts,
        IFoodCartUnitOfWork unitOfWork,
        IFoodModuleApi food,
        ICacheService cache)
    {
        _carts = carts;
        _unitOfWork = unitOfWork;
        _food = food;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(AddItemToFoodCartCommand command, CancellationToken cancellationToken)
    {
        var article = await _food.GetMenuItemAsync(command.RestaurantId, command.MenuItemId, cancellationToken);

        // MÊME RÉPONSE POUR « PLAT INCONNU » ET « PLAT D'UN AUTRE RESTAURANT ».
        //
        // Distinguer les deux dirait à qui essaie des identifiants lesquels
        // existent, et chez qui.
        if (article is null)
        {
            return Result.Failure<Guid>(Error.NotFound(
                "food_cart.item_not_found", "Ce plat n'est pas à la carte de cet établissement."));
        }

        if (!article.IsOrderable)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food_cart.item_unavailable", "Ce plat n'est pas commandable en ce moment."));
        }

        var cotation = Coter(article, command.Options);
        if (cotation.IsFailure)
        {
            return Result.Failure<Guid>(cotation.Error);
        }

        var cart = await _carts.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);

        if (cart is null)
        {
            var ouverture = CartAggregate.Create(command.BuyerId, command.RestaurantId, article.Currency);
            if (ouverture.IsFailure)
            {
                return Result.Failure<Guid>(ouverture.Error);
            }

            cart = ouverture.Value;
            await _carts.AddAsync(cart, cancellationToken);
        }

        var ajout = cart.AddItem(
            command.RestaurantId,
            command.MenuItemId,
            article.Name,
            cotation.Value,
            article.Currency,
            command.Quantity,
            command.Notes,
            command.Options.Select(o => (o.OptionGroupId, o.OptionId)).ToList());

        if (ajout.IsFailure)
        {
            return Result.Failure<Guid>(ajout.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(FoodCartCacheKeys.Active(command.BuyerId), cancellationToken);

        return Result.Success(cart.Id.Value);
    }

    /// <summary>
    /// Le prix unitaire du plat, suppléments compris — et la validation des choix.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// QUATRE CONTRÔLES, ET AUCUN N'EXISTAIT AVANT LA SÉPARATION.
    ///
    /// 1. CHAQUE OPTION APPARTIENT À UN GROUPE DE CE PLAT. Sans lui, l'identifiant
    ///    d'une option prise chez un autre plat — ou inventé — entrerait dans le
    ///    panier, et la cuisine recevrait un choix qu'elle ne sait pas préparer.
    ///
    /// 2. L'OPTION EST DISPONIBLE. « Grande taille » épuisée à 21 h ne doit pas se
    ///    commander à 21 h 01.
    ///
    /// 3. LES BORNES DU GROUPE SONT RESPECTÉES. Un groupe obligatoire sans choix
    ///    produirait un plat que la cuisine ne peut pas assembler ; trois
    ///    accompagnements dans un groupe qui en autorise un seul seraient
    ///    facturés et jamais servis.
    ///
    /// 4. LE MÊME CHOIX N'EST PAS COMPTÉ DEUX FOIS. Sans ce contrôle, envoyer
    ///    deux fois la même option la ferait payer deux fois tout en satisfaisant
    ///    un maximum de deux — un surcoût que rien ne rattrape.
    ///
    /// Le total est alors le prix de base plus la somme des écarts. C'est une
    /// ESTIMATION D'AFFICHAGE : la commande la recalcule au moment d'être passée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static Result<decimal> Coter(MenuItemView article, IReadOnlyList<FoodOptionChoice> choix)
    {
        var doublons = choix
            .GroupBy(c => c.OptionId)
            .Any(g => g.Count() > 1);

        if (doublons)
        {
            return Result.Failure<decimal>(Error.Validation(
                "food_cart.option_duplicated", "La même option a été choisie plusieurs fois."));
        }

        var montant = article.BasePrice;

        foreach (var c in choix)
        {
            var groupe = article.OptionGroups.FirstOrDefault(g => g.Id == c.OptionGroupId);
            var option = groupe?.Options.FirstOrDefault(o => o.Id == c.OptionId);

            if (groupe is null || option is null)
            {
                return Result.Failure<decimal>(Error.Validation(
                    "food_cart.option_unknown", "Une des options choisies n'existe pas pour ce plat."));
            }

            if (!option.IsAvailable)
            {
                return Result.Failure<decimal>(Error.Conflict(
                    "food_cart.option_unavailable", $"« {option.Name} » n'est pas disponible en ce moment."));
            }

            montant += option.PriceDelta;
        }

        foreach (var groupe in article.OptionGroups)
        {
            var retenues = choix.Count(c => c.OptionGroupId == groupe.Id);

            if (retenues < groupe.MinSelections)
            {
                return Result.Failure<decimal>(Error.Validation(
                    "food_cart.option_group_incomplete",
                    $"« {groupe.Name} » demande au moins {groupe.MinSelections} choix."));
            }

            // MaxSelections à zéro vaut « sans plafond » — un groupe qui n'en
            // impose pas ne doit pas devenir un groupe qui n'accepte rien.
            if (groupe.MaxSelections > 0 && retenues > groupe.MaxSelections)
            {
                return Result.Failure<decimal>(Error.Validation(
                    "food_cart.option_group_exceeded",
                    $"« {groupe.Name} » n'accepte pas plus de {groupe.MaxSelections} choix."));
            }
        }

        return Result.Success(montant);
    }
}

// ── Quantité, retrait, vidage ───────────────────────────────────────────────

public sealed record UpdateFoodCartLineQuantityCommand(Guid BuyerId, Guid LineId, int Quantity) : ICommand;

public sealed record RemoveFoodCartLineCommand(Guid BuyerId, Guid LineId) : ICommand;

public sealed record ClearFoodCartCommand(Guid BuyerId) : ICommand;

public sealed record ApplyFoodCartCouponCommand(Guid BuyerId, string Code) : ICommand;

public sealed record RemoveFoodCartCouponCommand(Guid BuyerId) : ICommand;

/// <summary>
/// Les mutations d'un panier existant. Elles partagent la même ouverture — lire
/// le panier actif, refuser s'il n'y en a pas — et la même fermeture : sauver,
/// puis faire tomber le cache.
///
/// L'INVALIDATION DU CACHE EST DANS LA FERMETURE COMMUNE, PAS DANS CHAQUE
/// GESTIONNAIRE.
///
/// `GetActiveFoodCartQuery` sert une copie mise en cache deux minutes. Un
/// gestionnaire qui oublierait l'invalidation ferait disparaître la modification
/// aux yeux du client, qui la retrouverait quelques minutes plus tard sans
/// comprendre. Écrite une fois, elle ne peut pas s'oublier cinq fois.
/// </summary>
internal abstract class FoodCartMutationHandler
{
    protected FoodCartMutationHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
    {
        Carts = carts;
        UnitOfWork = unitOfWork;
        Cache = cache;
    }

    protected IFoodCartRepository Carts { get; }

    protected IFoodCartUnitOfWork UnitOfWork { get; }

    protected ICacheService Cache { get; }

    protected async Task<Result> MuterAsync(
        Guid buyerId, Func<CartAggregate, Result> mutation, CancellationToken cancellationToken)
    {
        var cart = await Carts.GetActiveByBuyerAsync(buyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("food_cart.not_found", "Aucun panier de repas en cours."));
        }

        var resultat = mutation(cart);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await UnitOfWork.SaveChangesAsync(cancellationToken);
        await Cache.RemoveAsync(FoodCartCacheKeys.Active(buyerId), cancellationToken);

        return Result.Success();
    }
}

internal sealed class UpdateFoodCartLineQuantityCommandHandler
    : FoodCartMutationHandler, ICommandHandler<UpdateFoodCartLineQuantityCommand>
{
    public UpdateFoodCartLineQuantityCommandHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
        : base(carts, unitOfWork, cache)
    {
    }

    public Task<Result> Handle(UpdateFoodCartLineQuantityCommand command, CancellationToken cancellationToken)
        => MuterAsync(
            command.BuyerId,
            cart => cart.UpdateLineQuantity(command.LineId, command.Quantity),
            cancellationToken);
}

internal sealed class RemoveFoodCartLineCommandHandler
    : FoodCartMutationHandler, ICommandHandler<RemoveFoodCartLineCommand>
{
    public RemoveFoodCartLineCommandHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
        : base(carts, unitOfWork, cache)
    {
    }

    public Task<Result> Handle(RemoveFoodCartLineCommand command, CancellationToken cancellationToken)
        => MuterAsync(command.BuyerId, cart => cart.RemoveLine(command.LineId), cancellationToken);
}

internal sealed class ClearFoodCartCommandHandler
    : FoodCartMutationHandler, ICommandHandler<ClearFoodCartCommand>
{
    public ClearFoodCartCommandHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
        : base(carts, unitOfWork, cache)
    {
    }

    public Task<Result> Handle(ClearFoodCartCommand command, CancellationToken cancellationToken)
        => MuterAsync(command.BuyerId, cart => cart.Clear(), cancellationToken);
}

internal sealed class ApplyFoodCartCouponCommandHandler
    : FoodCartMutationHandler, ICommandHandler<ApplyFoodCartCouponCommand>
{
    private readonly IPricingModuleApi _pricing;

    public ApplyFoodCartCouponCommandHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache, IPricingModuleApi pricing)
        : base(carts, unitOfWork, cache)
        => _pricing = pricing;

    public async Task<Result> Handle(ApplyFoodCartCouponCommand command, CancellationToken cancellationToken)
    {
        // LA VALIDATION SE FAIT ICI, PAS DANS L'AGRÉGAT.
        //
        // Le panier ne doit pas savoir ce qu'est une promotion : seul Pricing
        // détient les codes, leurs fenêtres et leurs plafonds. `ApplyPromotionCode`
        // n'atteste donc de rien — il enregistre une saisie.
        //
        // LE SOUS-TOTAL N'EST PAS TRANSMIS, ET C'EST DÉLIBÉRÉ ICI.
        //
        // `ValidateCouponAsync` a gagné un paramètre `cartSubtotal` au lot D28 —
        // optionnel, pour que les appelants d'avant continuent de compiler. Le
        // panier de repas garde `NeutralPricingModuleApi`, qui refuse TOUT code
        // sans rien évaluer : lui passer un sous-total serait une donnée que
        // personne ne lit, et laisserait croire que la validation examine le
        // panier. Le jour où food-cart sera branché sur promotion-service, ce
        // paramètre sera à remplir dans le MÊME geste — sans quoi une condition
        // « panier d'au moins 5 000 F » ne serait découverte qu'au checkout.
        var verdict = await _pricing.ValidateCouponAsync(
            command.Code, command.BuyerId, cancellationToken: cancellationToken);
        if (!verdict.IsValid)
        {
            return Result.Failure(Error.Validation(
                verdict.ErrorCode ?? "pricing.coupon.invalid",
                verdict.ErrorMessage ?? "Ce code promo est invalide."));
        }

        return await MuterAsync(
            command.BuyerId, cart => cart.ApplyPromotionCode(command.Code), cancellationToken);
    }
}

internal sealed class RemoveFoodCartCouponCommandHandler
    : FoodCartMutationHandler, ICommandHandler<RemoveFoodCartCouponCommand>
{
    public RemoveFoodCartCouponCommandHandler(
        IFoodCartRepository carts, IFoodCartUnitOfWork unitOfWork, ICacheService cache)
        : base(carts, unitOfWork, cache)
    {
    }

    public Task<Result> Handle(RemoveFoodCartCouponCommand command, CancellationToken cancellationToken)
        => MuterAsync(command.BuyerId, cart => cart.RemovePromotionCode(), cancellationToken);
}
