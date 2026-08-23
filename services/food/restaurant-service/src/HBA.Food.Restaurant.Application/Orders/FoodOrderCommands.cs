using HBA.Food.Application.Abstractions;
using HBA.Food.Domain.Menus;
using HBA.Food.Domain.Orders;
using HBA.Food.Domain.Restaurants;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Application.Orders;

/// <summary>
/// Une ligne demandée, telle qu'elle arrive du panier : des IDENTIFIANTS, jamais
/// un prix.
///
/// LE PRIX N'EST PAS DANS CETTE STRUCTURE, ET C'EST TOUT L'ENJEU. Il est
/// calculé par <c>MenuItem.PriceSelection</c> au moment de la réception. Un prix
/// qui voyagerait depuis l'appelant serait un prix qu'on peut réécrire — c'est la
/// même règle que pour le prix acheteur d'une offre marketplace.
/// </summary>
public sealed record FoodOrderLineInput(
    Guid MenuItemId, int Quantity, IReadOnlyList<Guid> SelectedOptionIds, string? Notes);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE COMMANDE ARRIVE DANS LE RESTAURANT (cahier §10, §24).
///
/// Appelée depuis le composition root, à la réception d'un événement du module
/// Ordering — jamais par un client. Le §24 fixe l'enchaînement : Cart → Order →
/// Payment → Food Service → le restaurant reçoit.
///
/// IDEMPOTENTE PAR CONSTRUCTION. L'outbox promet « au moins une fois », pas
/// « exactement une fois » : le même événement sera rejoué. Sans le contrôle sur
/// <c>(Origin, OrderId)</c>, un rejeu créerait un second ticket, et la cuisine
/// préparerait deux fois le même repas.
///
/// `Origin` N'A PAS DE VALEUR PAR DÉFAUT, DÉLIBÉRÉMENT.
///
/// La colonne, elle, a un défaut — `Marketplace` — parce qu'il fallait décrire
/// les lignes déjà en base. Ici, un défaut transformerait l'oubli d'un futur
/// troisième pont en ticket mal classé, silencieusement. Sans défaut, l'oubli est
/// une erreur de compilation. C'est le seul endroit de la chaîne où l'on peut
/// encore l'attraper gratuitement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

/// <summary>
/// Une ligne repart en préparation — plat renversé, marquée prête par erreur.
///
/// SEUL RETOUR EN ARRIÈRE DU TICKET. Sans lui, une commande marquée prête par
/// mégarde appelle un livreur qui trouvera un passe vide, et le cuisinier n'a
/// aucun geste pour corriger.
/// </summary>
public sealed record ReopenKitchenItemCommand(Guid RestaurantId, Guid FoodOrderId, Guid ItemId) : ICommand;

public sealed record PrioritizeFoodOrderCommand(
    Guid RestaurantId, Guid FoodOrderId, int Priority) : ICommand;

// ── Sortie ──────────────────────────────────────────────────────────────────

/// <summary>
/// Le livreur a le sac.
/// </summary>
/// <remarks>
/// LE <c>RestaurantId</c> A ÉTÉ RETIRÉ, POUR LA RAISON EXACTE QUI L'EXCLUAIT
/// DÉJÀ DE <see cref="MarkFoodOrderDeliveredCommand"/>.
///
/// L'enlèvement est constaté par HBA Delivery, pas par le restaurant : c'est le
/// livreur qui déclare avoir chargé, depuis son application. Le composition root
/// ne dispose que de la référence <c>FOOD-…</c> de la course, qui porte
/// l'identifiant du TICKET ; exiger le restaurant l'aurait obligé à relire la
/// commande pour ne rien en faire d'autre.
///
/// La clôture par restaurant du §20 protège les gestes du PERSONNEL — accepter,
/// refuser, cuisiner. Elle n'a pas de sens sur un fait qu'un tiers constate.
/// </remarks>
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
        //
        // Rejouer l'événement doit rendre le même identifiant, pas une erreur :
        // l'expéditeur qui reçoit un échec réessaiera indéfiniment.
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
        //
        // Le panier l'a déjà vérifié — mais entre le panier et le paiement il
        // s'écoule des minutes, et le §20 exige d'« interdire une commande auprès
        // d'un restaurant fermé ou suspendu ». Un maquis qui ferme pendant le
        // paiement recevrait sinon une commande que personne ne verra jamais, et
        // le client attendrait un repas qui ne sera pas préparé.
        var blocage = restaurant.CanAcceptOrders(maintenant);
        if (blocage != OrderingBlockedReason.None)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "food.restaurant.not_accepting",
                $"L'établissement ne prend pas de commande actuellement ({blocage})."));
        }

        // LA CHARGE EST ÉVALUÉE AVANT DE CONSTRUIRE QUOI QUE CE SOIT (§14).
        //
        // Elle décide de trois choses d'un coup : refuser la commande, majorer le
        // délai annoncé, et couper l'auto-acceptation. Les calculer séparément
        // aurait laissé un chemin en oublier une — et c'est toujours celui qui
        // majore le délai qui manque.
        var charge = restaurant.AssessLoad(
            await _orders.CountActiveAsync(command.RestaurantId, cancellationToken));

        if (charge.BlocksNewOrders)
        {
            // L'« éventuellement » du §14, choisi par le restaurateur. Le message
            // dit « saturée », pas « fermée » : le client peut revenir dans dix
            // minutes, et lui faire croire le contraire le ferait partir ailleurs
            // pour la soirée.
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
        //
        // Le panier le contrôle déjà — mais un article peut avoir changé de prix
        // entre le panier et le paiement, et c'est le TOTAL RECALCULÉ qui compte.
        // Sans ce second contrôle, le restaurant recevrait une commande sous son
        // minimum et n'aurait plus qu'à la refuser, en perdant un client qui
        // croyait avoir payé.
        // « lignes.Count > 0 » N'EST PAS DE LA PRUDENCE DÉCORATIVE.
        //
        // Une commande sans ligne est refusée par l'agrégat, quelques lignes plus
        // bas. Mais le message ci-dessous lit la devise de la PREMIÈRE ligne : sans
        // ce test, une commande vide levait une IndexOutOfRange AVANT d'atteindre
        // le refus propre — et l'appelant recevait une erreur serveur là où il
        // aurait dû lire « une commande sans article n'a pas de sens ».
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

        // ═════════════════════════════════════════════════════════════════════
        // L'ACCEPTATION AUTOMATIQUE (§3), ET LES DEUX CONDITIONS QUI LA BRIDENT.
        //
        // Le mode doit être « Automatic » ET la cuisine ne doit pas être saturée.
        // Le cahier est explicite : « le mode automatique doit être suspendable en
        // cas de saturation ».
        //
        // C'EST UNE SUSPENSION, PAS UN CHANGEMENT DE RÉGLAGE. Le mode reste
        // « Automatic » en base et reprend de lui-même quand la cuisine se vide.
        // Le réécrire obligerait quelqu'un à le remettre à la main après le coup de
        // feu — et personne n'y penserait avant de constater que plus rien ne part
        // tout seul.
        //
        // L'acceptation se fait dans la MÊME transaction que la réception : une
        // commande enregistrée mais non acceptée par un incident resterait en
        // attente d'une décision que personne n'a l'intention de prendre.
        // ═════════════════════════════════════════════════════════════════════
        if (restaurant.AcceptanceMode == OrderAcceptanceMode.Automatic && !charge.AutoAcceptSuspended)
        {
            commande.Value.AcceptAutomatically(maintenant, charge.ExtraWaitMinutes);
        }

        await _orders.AddAsync(commande.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return commande.Value.Id.Value;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE SNAPSHOT SE FABRIQUE ICI, ET NULLE PART AILLEURS (§13).
    ///
    /// L'article est relu dans la carte, son prix RECALCULÉ par le domaine, puis
    /// tout est recopié dans la ligne. Après cet instant, la commande ne consulte
    /// plus jamais la carte — c'est ce qui rend vraie la phrase du cahier :
    /// « une modification ultérieure du menu ne change jamais une commande déjà
    /// passée ».
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

        // L'ARTICLE DOIT ÊTRE DE CE RESTAURANT. Sans cette comparaison, un
        // panier bricolé ferait préparer le plat d'un concurrent, au prix du
        // concurrent, dans une cuisine qui ne l'a jamais mis à sa carte.
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

    /// <summary>
    /// L'ACCEPTATION MANUELLE MAJORE L'ETA COMME L'AUTOMATIQUE.
    ///
    /// Il aurait été facile de ne brancher la charge que sur le chemin
    /// automatique — c'est celui qu'on venait d'écrire. Le résultat aurait été un
    /// délai honnête quand la machine accepte et un délai optimiste quand un
    /// caissier accepte, dans la même cuisine, à la même minute. Le client n'aurait
    /// eu aucun moyen de comprendre pourquoi.
    /// </summary>
    public async Task<Result> Handle(AcceptFoodOrderCommand command, CancellationToken cancellationToken)
    {
        var attente = await EstimateExtraWaitAsync(command.RestaurantId, cancellationToken);

        return await OnOrderAsync(command.FoodOrderId, command.RestaurantId, cancellationToken,
            o => o.Accept(command.ActorUserId, DateTime.UtcNow, attente));
    }

    /// <summary>
    /// L'attente supplémentaire due à la charge (§14).
    ///
    /// Rend zéro si l'établissement a disparu : refuser une acceptation parce
    /// qu'on n'a pas su calculer une majoration serait bloquer une cuisine pour un
    /// détail d'affichage.
    /// </summary>
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
    /// SANS RestaurantId, COMME LA LIVRAISON : ces deux faits-là sont constatés
    /// par HBA Delivery, pas par le restaurant. Voir
    /// <see cref="MarkFoodOrderPickedUpCommand"/>.
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
    /// SANS RestaurantId : la livraison est constatée par HBA Delivery, pas par
    /// le restaurant. Le composition root n'a que l'identifiant de la commande
    /// Food, et exiger le restaurant l'obligerait à le relire pour rien.
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

    /// <summary>
    /// PAR <c>OrderId</c> ET NON PAR <c>FoodOrderId</c>, délibérément.
    ///
    /// L'annulation vient d'Ordering ou de l'exploitation, qui ne connaissent que
    /// la commande commerciale. Les obliger à traduire d'abord ferait une lecture
    /// de plus sur un chemin qui doit rester court : entre l'annulation et l'arrêt
    /// de la cuisine, chaque seconde est un plat engagé.
    ///
    /// Une commande absente n'est PAS une erreur : le restaurant n'a peut-être
    /// jamais été raccordé à cette commande.
    /// </summary>
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

    /// <summary>
    /// LA CLÔTURE PAR RESTAURANT, ICI COMME PARTOUT.
    ///
    /// Le §20 l'exige : « restreindre chaque employé à son RestaurantId ». Le
    /// restaurant vient du jeton, l'identifiant de commande du client — sans la
    /// comparaison, un caissier accepterait ou refuserait les commandes d'un
    /// concurrent, et lui ferait perdre son chiffre d'affaires du soir.
    /// </summary>
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
