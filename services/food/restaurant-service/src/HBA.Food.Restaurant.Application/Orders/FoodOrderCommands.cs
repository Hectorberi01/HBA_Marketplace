using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Orders;

/// <summary>
/// Une ligne demandée, telle qu'elle arrive du panier : des IDENTIFIANTS, jamais un
/// prix.
/// </summary>
public sealed record FoodOrderLineInput(
    Guid MenuItemId, int Quantity, IReadOnlyList<Guid> SelectedOptionIds, string? Notes);

/// <summary>UNE COMMANDE ARRIVE DANS LE RESTAURANT (cahier §10, §24).</summary>
public sealed record ReceiveFoodOrderCommand(
    FoodOrderOrigin Origin,
    Guid OrderId,
    Guid RestaurantId,
    IReadOnlyList<FoodOrderLineInput> Lines,
    string? CustomerNote) : ICommand<Guid>;

// ── Décision du restaurant (§11, §18) ───────────────────────────────────────

public sealed record AcceptFoodOrderCommand(
    Guid RestaurantId, Guid ActorUserId, Guid FoodOrderId) : ICommand;

public sealed record RejectFoodOrderCommand(
    Guid RestaurantId, Guid ActorUserId, Guid FoodOrderId,
    FoodRejectionReason Reason, string? Comment) : ICommand;

// ── Cuisine (§13, §18) ──────────────────────────────────────────────────────

public sealed record StartKitchenTicketCommand(Guid RestaurantId, Guid FoodOrderId) : ICommand;

public sealed record StartKitchenItemCommand(Guid RestaurantId, Guid FoodOrderId, Guid ItemId) : ICommand;

public sealed record MarkKitchenItemReadyCommand(Guid RestaurantId, Guid FoodOrderId, Guid ItemId) : ICommand;

public sealed record MarkKitchenTicketReadyCommand(Guid RestaurantId, Guid FoodOrderId) : ICommand;

/// <summary>Une ligne repart en préparation — plat renversé, marquée prête par erreur.</summary>
public sealed record ReopenKitchenItemCommand(Guid RestaurantId, Guid FoodOrderId, Guid ItemId) : ICommand;

public sealed record PrioritizeFoodOrderCommand(
    Guid RestaurantId, Guid FoodOrderId, int Priority) : ICommand;

// ── Sortie ──────────────────────────────────────────────────────────────────

/// <summary>Le livreur a le sac.</summary>
public sealed record MarkFoodOrderPickedUpCommand(Guid FoodOrderId) : ICommand;

public sealed record MarkFoodOrderDeliveredCommand(Guid FoodOrderId) : ICommand;

public sealed record CancelFoodOrderCommand(
    FoodOrderOrigin Origin, Guid OrderId, string? Reason) : ICommand;

internal sealed class FoodOrderCommandHandler
    : ICommandHandler<ReceiveFoodOrderCommand, Guid>,
      ICommandHandler<AcceptFoodOrderCommand>,
      ICommandHandler<RejectFoodOrderCommand>,
      ICommandHandler<StartKitchenTicketCommand>,
      ICommandHandler<StartKitchenItemCommand>,
      ICommandHandler<MarkKitchenItemReadyCommand>,
      ICommandHandler<MarkKitchenTicketReadyCommand>,
      ICommandHandler<ReopenKitchenItemCommand>,
      ICommandHandler<PrioritizeFoodOrderCommand>,
      ICommandHandler<MarkFoodOrderPickedUpCommand>,
      ICommandHandler<MarkFoodOrderDeliveredCommand>,
      ICommandHandler<CancelFoodOrderCommand>
{
    private readonly IFoodOrderRepository _orders;
    private readonly IRestaurantRepository _restaurants;
    private readonly IMenuItemRepository _items;
    private readonly IFoodUnitOfWork _unitOfWork;

    public FoodOrderCommandHandler(
        IFoodOrderRepository orders,
        IRestaurantRepository restaurants,
        IMenuItemRepository items,
        IFoodUnitOfWork unitOfWork)
    {
        _orders = orders;
        _restaurants = restaurants;
        _items = items;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(ReceiveFoodOrderCommand command, CancellationToken cancellationToken)
    {
        // L'IDEMPOTENCE D'ABORD, AVANT TOUT TRAVAIL.
        var existante = await _orders.GetByOrderIdAsync(
            command.Origin, command.OrderId, cancellationToken);
        if (existante is not null)
        {
            return existante.Id.Value;
        }

        var restaurant = await _restaurants.GetByIdAsync(
            new RestaurantId(command.RestaurantId), cancellationToken);

        if (restaurant is null)
        {
            return Result.Failure<Guid>(
                Error.NotFound("food.restaurant.not_found", "Établissement introuvable."));
        }

        var maintenant = DateTime.UtcNow;

        // LE RESTAURANT DOIT POUVOIR PRENDRE LA COMMANDE, MÊME ICI.
        var blocage = restaurant.CanAcceptOrders(maintenant);
        if (blocage != OrderingBlockedReason.None)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.restaurant.not_accepting",
                $"L'établissement ne prend pas de commande actuellement ({blocage})."));
        }

        // LA CHARGE EST ÉVALUÉE AVANT DE CONSTRUIRE QUOI QUE CE SOIT (§14).
        var charge = restaurant.AssessLoad(
            await _orders.CountActiveAsync(command.RestaurantId, cancellationToken));

        if (charge.BlocksNewOrders)
        {
            // L'« éventuellement » du §14, choisi par le restaurateur.
            return Result.Failure<Guid>(Error.Conflict(
                "food.restaurant.saturated",
                "La cuisine est saturée et n'accepte plus de nouvelle commande pour l'instant."));
        }

        var lignes = new List<FoodOrderItem>();

        foreach (var demande in command.Lines)
        {
            var ligne = await BuildLineAsync(command.RestaurantId, demande, restaurant, maintenant, cancellationToken);
            if (ligne.IsFailure)
            {
                return Result.Failure<Guid>(ligne.Error);
            }

            lignes.Add(ligne.Value);
        }

        // LE MINIMUM DE COMMANDE EST VÉRIFIÉ ICI AUSSI (§3, §15).
        var total = lignes.Sum(l => l.LineTotal);

        if (lignes.Count > 0 && restaurant.MinimumOrderAmount is { } minimum && total < minimum)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.order.below_minimum",
                $"Le minimum de commande de cet établissement est de {minimum:0} {lignes[0].Currency}."));
        }

        var commande = FoodOrder.Receive(
            command.Origin, command.OrderId, command.RestaurantId, lignes,
            command.CustomerNote, maintenant);

        if (commande.IsFailure)
        {
            return Result.Failure<Guid>(commande.Error);
        }

        // L'ACCEPTATION AUTOMATIQUE (§3), ET LES DEUX CONDITIONS QUI LA BRIDENT.
        if (restaurant.AcceptanceMode == OrderAcceptanceMode.Automatic && !charge.AutoAcceptSuspended)
        {
            commande.Value.AcceptAutomatically(maintenant, charge.ExtraWaitMinutes);
        }

        await _orders.AddAsync(commande.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return commande.Value.Id.Value;
    }

    /// <summary>LE SNAPSHOT SE FABRIQUE ICI, ET NULLE PART AILLEURS (§13).</summary>
    private async Task<Result<FoodOrderItem>> BuildLineAsync(
        Guid restaurantId,
        FoodOrderLineInput demande,
        Restaurant restaurant,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (demande.Quantity <= 0)
        {
            return Error.Validation("food.order.quantity_invalid", "La quantité doit être positive.");
        }

        var article = await _items.GetByIdAsync(new MenuItemId(demande.MenuItemId), cancellationToken);

        // L'ARTICLE DOIT ÊTRE DE CE RESTAURANT. Sans cette comparaison, un panier
        // bricolé ferait préparer le plat d'un concurrent, au prix du concurrent,
        // dans une cuisine qui ne l'a jamais mis à sa carte.
        if (article is null || article.RestaurantId != restaurantId)
        {
            return Error.NotFound("food.item.not_found", "Article introuvable dans cet établissement.");
        }

        // Le prix sort du DOMAINE, options comprises, et la sélection est validée
        // au passage : options d'un autre plat refusées, minimum et maximum de
        // chaque groupe vérifiés, articles épuisés rejetés.
        var tarif = article.PriceSelection(demande.SelectedOptionIds, nowUtc);
        if (tarif.IsFailure)
        {
            return tarif.Error;
        }

        var options = tarif.Value.Options
            .Select(o => new FoodOrderItemOption(Guid.NewGuid(), o.OptionId, o.GroupName, o.OptionName, o.PriceDelta))
            .ToList();

        return new FoodOrderItem(
            Guid.NewGuid(),
            article.Id.Value,
            article.Name,
            tarif.Value.UnitPrice,
            tarif.Value.Currency,
            demande.Quantity,
            demande.Notes,
            article.PreparationStationId,

            // À défaut de temps propre à l'article, celui du restaurant : le §14
            // veut un MAX sur des valeurs comparables, et un zéro par défaut
            // annoncerait une commande prête immédiatement.
            article.PreparationMinutes ?? restaurant.PreparationMinutes,
            options);
    }

    // ── Décision ────────────────────────────────────────────────────────────

    /// <summary>L'ACCEPTATION MANUELLE MAJORE L'ETA COMME L'AUTOMATIQUE.</summary>
    public async Task<Result> Handle(AcceptFoodOrderCommand command, CancellationToken cancellationToken)
    {
        var attente = await EstimateExtraWaitAsync(command.RestaurantId, cancellationToken);

        return await OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.Accept(command.ActorUserId, DateTime.UtcNow, attente));
    }

    /// <summary>L'attente supplémentaire due à la charge (§14).</summary>
    private async Task<int> EstimateExtraWaitAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        var restaurant = await _restaurants.GetByIdAsync(new RestaurantId(restaurantId), cancellationToken);
        if (restaurant is null)
        {
            return 0;
        }

        var actives = await _orders.CountActiveAsync(restaurantId, cancellationToken);
        return restaurant.AssessLoad(actives).ExtraWaitMinutes;
    }

    public Task<Result> Handle(RejectFoodOrderCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.Reject(command.ActorUserId, command.Reason, command.Comment, DateTime.UtcNow));

    // ── Cuisine ─────────────────────────────────────────────────────────────

    public Task<Result> Handle(StartKitchenTicketCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.StartAll(DateTime.UtcNow));

    public Task<Result> Handle(StartKitchenItemCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.StartItem(command.ItemId, DateTime.UtcNow));

    public Task<Result> Handle(MarkKitchenItemReadyCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.MarkItemReady(command.ItemId, DateTime.UtcNow));

    public Task<Result> Handle(MarkKitchenTicketReadyCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.MarkAllReady(DateTime.UtcNow));

    public Task<Result> Handle(ReopenKitchenItemCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.ReopenItem(command.ItemId, DateTime.UtcNow));

    public Task<Result> Handle(PrioritizeFoodOrderCommand command, CancellationToken cancellationToken)
        => OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.SetPriority(command.Priority));

    // ── Sortie ──────────────────────────────────────────────────────────────

    /// <summary>
    /// SANS RestaurantId, COMME LA LIVRAISON : ces deux faits-là sont constatés par
    /// HBA Delivery, pas par le restaurant.
    /// </summary>
    public async Task<Result> Handle(MarkFoodOrderPickedUpCommand command, CancellationToken cancellationToken)
    {
        var commande = await _orders.GetByIdAsync(new FoodOrderId(command.FoodOrderId), cancellationToken);
        if (commande is null)
        {
            return Result.Failure(Introuvable);
        }

        return await CommitAsync(commande.MarkPickedUp(DateTime.UtcNow), cancellationToken);
    }

    /// <summary>
    /// SANS RestaurantId : la livraison est constatée par HBA Delivery, pas par le
    /// restaurant.
    /// </summary>
    public async Task<Result> Handle(MarkFoodOrderDeliveredCommand command, CancellationToken cancellationToken)
    {
        var commande = await _orders.GetByIdAsync(new FoodOrderId(command.FoodOrderId), cancellationToken);
        if (commande is null)
        {
            return Result.Failure(Introuvable);
        }

        return await CommitAsync(commande.MarkDelivered(), cancellationToken);
    }

    /// <summary>PAR <c>OrderId</c> ET NON PAR <c>FoodOrderId</c>, délibérément.</summary>
    public async Task<Result> Handle(CancelFoodOrderCommand command, CancellationToken cancellationToken)
    {
        var commande = await _orders.GetByOrderIdAsync(
            command.Origin, command.OrderId, cancellationToken);
        if (commande is null)
        {
            return Result.Success();
        }

        return await CommitAsync(commande.Cancel(command.Reason), cancellationToken);
    }

    // ── Chargement scopé ────────────────────────────────────────────────────

    private static readonly Error Introuvable =
        Error.NotFound("food.order.not_found", "Commande introuvable.");

    /// <summary>LA CLÔTURE PAR RESTAURANT, ICI COMME PARTOUT.</summary>
    private async Task<Result> OnOrderAsync(
        Guid foodOrderId, Guid restaurantId, CancellationToken cancellationToken, Func<FoodOrder, Result> action)
    {
        var commande = await _orders.GetByIdAsync(new FoodOrderId(foodOrderId), cancellationToken);
        if (commande is null || commande.RestaurantId != restaurantId)
        {
            return Result.Failure(Introuvable);
        }

        return await CommitAsync(action(commande), cancellationToken);
    }

    private async Task<Result> CommitAsync(Result result, CancellationToken cancellationToken)
    {
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
