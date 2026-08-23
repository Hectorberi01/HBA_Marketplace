using HBA.Food.Domain.Orders.Events;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Food.Domain.Orders;

public readonly record struct FoodOrderId(Guid Value)
{
    public static FoodOrderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Le refus, et ce qui permet d'en rendre compte (cahier §11).</summary>
public sealed record FoodOrderRejection(
    FoodRejectionReason Reason, string? Comment, Guid RejectedByUserId, DateTime RejectedAtUtc);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PART OPÉRATIONNELLE D'UNE COMMANDE (cahier des charges §10 à §14).
///
/// LE MODULE ORDERING RESTE PROPRIÉTAIRE DE LA COMMANDE COMMERCIALE.
///
/// Cet agrégat ne connaît d'elle qu'un <c>OrderId</c>, et ne saura jamais si elle
/// est payée ou remboursée. Il sait si le restaurant a dit oui, si la cuisine a
/// commencé, si le sac est sur le passe. Les deux se rejoignent par événements.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE TICKET DE CUISINE (§12) N'EST PAS UN AGRÉGAT SÉPARÉ, ET C'EST UN CHOIX.
///
/// Le cahier liste <c>KitchenTicket</c> comme une entité à part. Ici, le ticket
/// EST la commande vue depuis la cuisine : mêmes lignes, même identifiant, même
/// ligne en base.
///
/// La raison est dans le §20 : « garantir une seule transition de statut valide
/// en cas d'actions concurrentes ». Deux agrégats auraient signifié deux verrous
/// et une cohérence différée entre « ticket prêt » et « commande prête ». Un
/// livreur appelé sur l'un pendant que l'autre n'a pas basculé, c'est une course
/// perdue — et le décalage n'aurait duré que quelques millisecondes, donc
/// personne ne l'aurait jamais reproduit.
///
/// Un seul verrou optimiste, une seule vérité. Le vocabulaire du cahier est
/// conservé (<c>KitchenStatus</c>, <c>StartedAtUtc</c>, <c>ReadyAtUtc</c>) pour que
/// la correspondance reste lisible.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class FoodOrder : AggregateRoot<FoodOrderId>
{
    private readonly List<FoodOrderItem> _items = new();

    private FoodOrder()
    {
    }

    private FoodOrder(
        FoodOrderId id, FoodOrderOrigin origin, Guid orderId, Guid restaurantId,
        string? customerNote, DateTime nowUtc)
        : base(id)
    {
        Origin = origin;
        OrderId = orderId;
        RestaurantId = restaurantId;
        CustomerNote = customerNote;
        Status = FoodOrderStatus.PendingRestaurantAcceptance;
        ReceivedAtUtc = nowUtc;
    }

    /// <summary>
    /// De quel univers vient <see cref="OrderId"/>. Voir <see cref="FoodOrderOrigin"/>
    /// pour ce que son absence coûtait.
    /// </summary>
    public FoodOrderOrigin Origin { get; private set; }

    /// <summary>
    /// La commande commerciale — chez Ordering OU chez FoodOrders, selon
    /// <see cref="Origin"/>.
    ///
    /// CE COMMENTAIRE DISAIT « chez Ordering », ET C'ÉTAIT FAUX POUR LA MOITIÉ
    /// DES TICKETS. Deux ponts écrivent ici, avec des identifiants venant de deux
    /// agrégats distincts. Lire ce champ sans lire `Origin` revient à interroger
    /// une base au hasard.
    ///
    /// UNIQUE DANS SON UNIVERS : une commande n'a qu'une part cuisine. L'index
    /// unique en base porte donc `(Origin, OrderId)` et non `OrderId` seul — il
    /// est le rempart contre le double traitement d'un événement, l'outbox
    /// promettant « au moins une fois », pas « exactement une fois ».
    /// </summary>
    public Guid OrderId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public FoodOrderStatus Status { get; private set; }

    /// <summary>Instructions du client pour l'ensemble : « sonner fort », « sans couverts ».</summary>
    public string? CustomerNote { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    /// <summary>Qui a accepté, et quand. Le cahier (§21) demande de tracer l'acceptation.</summary>
    public Guid? AcceptedByUserId { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }

    public FoodOrderRejection? Rejection { get; private set; }

    /// <summary>Première ligne touchée par la cuisine. Alimente <c>food_preparation_time_seconds</c> (§21).</summary>
    public DateTime? StartedAtUtc { get; private set; }

    /// <summary>Instant où TOUTES les lignes ont été prêtes — toutes stations confondues.</summary>
    public DateTime? ReadyAtUtc { get; private set; }

    public DateTime? PickedUpAtUtc { get; private set; }

    /// <summary>
    /// Délai annoncé à l'acceptation, en minutes.
    ///
    /// Calculé selon le §14 : MAX des temps de préparation des lignes. Le maximum
    /// et non la somme — les plats se préparent en parallèle. Sommer annoncerait
    /// une heure et demie pour trois plats de trente minutes.
    /// </summary>
    public int? EstimatedPreparationMinutes { get; private set; }

    /// <summary>
    /// Priorité d'affichage sur l'écran de cuisine (§12). Plus haut = plus urgent.
    ///
    /// Sert à remonter une commande en retard, ou celle d'un client qui attend sur
    /// place. Sans elle, l'écran est strictement chronologique et rien ne permet
    /// de rattraper un oubli.
    /// </summary>
    public int Priority { get; private set; }

    public IReadOnlyCollection<FoodOrderItem> Items => _items.AsReadOnly();

    /// <summary>
    /// L'état du ticket, DÉRIVÉ de ses lignes — jamais stocké.
    ///
    /// C'EST CE QUI REND LE §13 VRAI : « la commande globale est prête si toutes
    /// les stations sont READY ». Une colonne de statut posée à la main pourrait
    /// contredire ses propres lignes, et le grillardin qui termine marquerait toute
    /// la commande prête — le livreur repartirait sans les boissons.
    /// </summary>
    public KitchenTicketStatus KitchenStatus
    {
        get
        {
            if (Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
            {
                return KitchenTicketStatus.Cancelled;
            }

            if (_items.Count > 0 && _items.All(i => i.Status == KitchenItemStatus.Ready))
            {
                return KitchenTicketStatus.Ready;
            }

            return _items.Any(i => i.Status != KitchenItemStatus.Pending)
                ? KitchenTicketStatus.Preparing
                : KitchenTicketStatus.Pending;
        }
    }

    /// <summary>Le total de la part restauration. Somme des lignes déjà figées.</summary>
    public decimal Total => _items.Sum(i => i.LineTotal);

    /// <summary>Les postes concernés par cette commande. Vide = aucun poste déclaré.</summary>
    public IReadOnlyCollection<Guid> Stations
        => _items.Where(i => i.PreparationStationId is not null)
            .Select(i => i.PreparationStationId!.Value)
            .Distinct()
            .ToList();

    // ── Réception ───────────────────────────────────────────────────────────

    /// <summary>
    /// Une commande arrive du module Ordering.
    ///
    /// LES LIGNES SONT DES SNAPSHOTS FOURNIS PAR L'APPELANT, pas des articles
    /// relus dans la carte. C'est la règle du §13, et elle a une conséquence
    /// pratique : cet agrégat ne consulte JAMAIS <c>MenuItem</c>. Une carte
    /// modifiée pendant la préparation ne peut donc rien changer à ce qui est en
    /// cuisson.
    /// </summary>
    public static Result<FoodOrder> Receive(
        FoodOrderOrigin origin,
        Guid orderId,
        Guid restaurantId,
        IReadOnlyList<FoodOrderItem> items,
        string? customerNote,
        DateTime nowUtc)
    {
        if (orderId == Guid.Empty || restaurantId == Guid.Empty)
        {
            return Error.Validation(
                "food.order.parent_required", "La commande doit référencer une commande et un restaurant.");
        }

        if (items.Count == 0)
        {
            // Une commande vide n'apparaîtrait sur aucun écran de cuisine, resterait
            // « à accepter » pour toujours, et personne ne saurait dire pourquoi.
            return Error.Validation("food.order.empty", "Une commande sans article n'a pas de sens.");
        }

        var commande = new FoodOrder(
            FoodOrderId.New(), origin, orderId, restaurantId,
            string.IsNullOrWhiteSpace(customerNote) ? null : customerNote.Trim(), nowUtc);

        commande._items.AddRange(items);

        // L'origine voyage avec le fait : tout ce qui écoutera ce ticket devra
        // savoir à quelle base poser ses questions, et le rappeler à chaque
        // gestionnaire par une lecture en base serait un aller-retour de plus pour
        // une donnée qui ne change jamais.
        commande.Raise(new FoodOrderReceivedDomainEvent(
            commande.Id.Value, origin, orderId, restaurantId, commande.Total, items.Count));

        return commande;
    }

    // ── Décision du restaurant ──────────────────────────────────────────────

    /// <summary>
    /// Le restaurant accepte : le ticket de cuisine existe (§12).
    ///
    /// UNE COMMANDE ANNULÉE NE S'ACCEPTE PAS — le §20 l'exige nommément, et le
    /// cas est réel : le client annule pendant que le caissier a l'écran ouvert.
    /// Sans cette garde, la cuisine prépare un repas que personne ne viendra
    /// chercher et que personne ne paiera.
    /// </summary>
    public Result Accept(Guid actorUserId, DateTime nowUtc, int extraWaitMinutes = 0)
        => AcceptInternal(actorUserId, nowUtc, extraWaitMinutes);

    /// <summary>
    /// Acceptation AUTOMATIQUE (§3, mode <c>Automatic</c>).
    ///
    /// MÉTHODE DISTINCTE, PAS UN <c>Guid.Empty</c> PASSÉ À <see cref="Accept"/>.
    ///
    /// Le cahier (§21) demande de tracer l'acceptation. Un identifiant vide dans la
    /// colonne « accepté par » se lit « on ne sait pas qui », alors que la vérité
    /// est « personne, et c'était voulu ». Le jour où un client conteste, la
    /// distinction fait toute la différence : nul n'a regardé cette commande.
    /// </summary>
    public Result AcceptAutomatically(DateTime nowUtc, int extraWaitMinutes = 0)
    {
        var result = AcceptInternal(actorUserId: null, nowUtc, extraWaitMinutes);
        if (result.IsSuccess)
        {
            WasAutoAccepted = true;
        }

        return result;
    }

    /// <summary>Acceptée sans qu'aucun humain ne l'ait regardée. Voir <see cref="AcceptAutomatically"/>.</summary>
    public bool WasAutoAccepted { get; private set; }

    private Result AcceptInternal(Guid? actorUserId, DateTime nowUtc, int extraWaitMinutes)
    {
        if (Status == FoodOrderStatus.Accepted)
        {
            // Idempotent : deux caissiers sur le même écran ne doivent pas produire
            // une erreur incompréhensible pour le second.
            return Result.Success();
        }

        if (Status != FoodOrderStatus.PendingRestaurantAcceptance)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_pending",
                Status == FoodOrderStatus.Cancelled
                    ? "Cette commande a été annulée : elle ne peut plus être acceptée."
                    : "Cette commande n'attend plus de décision du restaurant."));
        }

        Status = FoodOrderStatus.Accepted;
        AcceptedByUserId = actorUserId;
        AcceptedAtUtc = nowUtc;

        // ═════════════════════════════════════════════════════════════════════
        // §14 : ETA = MAX(temps des articles) + temps d'attente estimé.
        //
        // Le MAXIMUM et non la somme — les plats se préparent en parallèle.
        //
        // L'ATTENTE VIENT DE LA CHARGE, ET ELLE EST FOURNIE PAR L'APPELANT.
        // Cet agrégat ne compte pas les commandes en cours : elles sont d'autres
        // racines. C'est `Restaurant.AssessLoad` qui la calcule, à partir du
        // rythme du restaurant lui-même.
        //
        // Sans elle, une cuisine à quinze commandes annonçait le même délai qu'à
        // vide — et le client apprenait la vérité en attendant.
        // ═════════════════════════════════════════════════════════════════════
        EstimatedPreparationMinutes = _items.Max(i => i.PreparationMinutes) + Math.Max(0, extraWaitMinutes);

        Raise(new FoodOrderAcceptedDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, actorUserId, EstimatedPreparationMinutes.Value));

        return Result.Success();
    }

    /// <summary>
    /// Le restaurant refuse, avec un motif (§11).
    ///
    /// LE MOTIF EST OBLIGATOIRE ET ÉNUMÉRÉ, le commentaire facultatif. C'est ce qui
    /// rend calculable « les restaurants à fort taux de refus » (§22) — un champ
    /// libre aurait fait de « plus de poulet » et « rupture » deux motifs distincts.
    /// </summary>
    public Result Reject(Guid actorUserId, FoodRejectionReason reason, string? comment, DateTime nowUtc)
    {
        if (Status != FoodOrderStatus.PendingRestaurantAcceptance)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_pending", "Cette commande n'attend plus de décision du restaurant."));
        }

        Status = FoodOrderStatus.Rejected;
        Rejection = new FoodOrderRejection(
            reason, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), actorUserId, nowUtc);

        Raise(new FoodOrderRejectedDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, reason.ToString(), Rejection.Comment, actorUserId));

        return Result.Success();
    }

    // ── Cuisine ─────────────────────────────────────────────────────────────

    /// <summary>Le cuisinier commence une ligne.</summary>
    public Result StartItem(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.Start(), nowUtc);

    /// <summary>« Commencer » sur tout le ticket — le bouton du §13.</summary>
    public Result StartAll(DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        foreach (var ligne in _items)
        {
            ligne.Start();
        }

        EnterPreparation(nowUtc);
        return Result.Success();
    }

    /// <summary>Une ligne est prête. Les autres postes peuvent encore travailler.</summary>
    public Result MarkItemReady(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.MarkReady(), nowUtc);

    /// <summary>
    /// Une ligne repart en préparation — plat renversé, marquée prête par erreur.
    ///
    /// C'EST LE SEUL RETOUR EN ARRIÈRE DU TICKET, et il est nécessaire : sans
    /// lui, une commande marquée prête par mégarde appelle un livreur qui trouvera
    /// un passe vide, et le cuisinier n'a aucun geste pour corriger.
    ///
    /// La commande quitte alors « prête » — voir <see cref="SettleReadiness"/>.
    /// </summary>
    public Result ReopenItem(Guid itemId, DateTime nowUtc)
        => OnKitchenItem(itemId, ligne => ligne.Reopen(), nowUtc);

    /// <summary>Tout le ticket est prêt.</summary>
    public Result MarkAllReady(DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        foreach (var ligne in _items)
        {
            ligne.MarkReady();
        }

        EnterPreparation(nowUtc);
        SettleReadiness(nowUtc);

        return Result.Success();
    }

    /// <summary>Remonte ou redescend la commande sur l'écran de cuisine.</summary>
    public Result SetPriority(int priority)
    {
        Priority = priority;
        return Result.Success();
    }

    // ── Sortie ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Le livreur emporte le sac.
    ///
    /// SEULEMENT DEPUIS « PRÊT ». Le §20 interdit d'atteindre l'aval sans le
    /// workflow de préparation ; laisser passer un enlèvement sur une commande non
    /// prête, c'est un livreur qui part avec un sac incomplet — et le client
    /// découvre le manque chez lui.
    /// </summary>
    public Result MarkPickedUp(DateTime nowUtc)
    {
        if (Status == FoodOrderStatus.PickedUp)
        {
            return Result.Success();
        }

        if (Status != FoodOrderStatus.ReadyForPickup)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_ready", "Cette commande n'est pas prête à être enlevée."));
        }

        Status = FoodOrderStatus.PickedUp;
        PickedUpAtUtc = nowUtc;

        Raise(new FoodOrderPickedUpDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// Le repas est entre les mains du client. État terminal.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CETTE MÉTHODE NE LEVAIT AUCUN ÉVÉNEMENT, ET LE RESTAURATEUR N'ÉTAIT
    ///    JAMAIS PAYÉ.
    ///
    /// Elle basculait le statut, rendait un succès, et se taisait. Toutes ses
    /// voisines — acceptation, refus, mise en préparation, mise à disposition,
    /// enlèvement, annulation — publient leur fait ; celle-ci, non. Le repas
    /// était remis au client et RIEN hors du module ne l'apprenait : la commande
    /// commerciale restait « confirmée », donc <c>OrderDelivered</c> n'était
    /// jamais publié, donc l'escrow n'était pas levé et le gain du restaurateur
    /// restait bloqué en « à venir ».
    ///
    /// C'est le prolongement d'une asymétrie posée en amont : on avait branché la
    /// fin d'une course <c>ORDER-</c> sans brancher celle des courses
    /// <c>FOOD-</c> créées au même moment. Le prochain préfixe posera le même
    /// piège — un état terminal muet ne casse ni la compilation ni les tests.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Result MarkDelivered()
    {
        if (Status == FoodOrderStatus.Delivered)
        {
            // Idempotent : la fin de course peut être rejouée par l'outbox.
            return Result.Success();
        }

        if (Status != FoodOrderStatus.PickedUp)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_picked_up", "Cette commande n'a pas encore été enlevée."));
        }

        Status = FoodOrderStatus.Delivered;

        Raise(new FoodOrderDeliveredDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// La commande est annulée — par le client, l'exploitation, ou un incident.
    ///
    /// REFUSÉE UNE FOIS LE SAC PARTI. Après l'enlèvement, le repas est fait et
    /// hors du restaurant : ce qui se joue alors est un remboursement, et ce n'est
    /// pas la décision de cet agrégat. Prétendre annuler ferait croire à la cuisine
    /// qu'elle peut s'arrêter, alors qu'il n'y a plus rien à arrêter.
    /// </summary>
    public Result Cancel(string? reason)
    {
        // « DÉJÀ REFUSÉE » EST UN SUCCÈS, AU MÊME TITRE QUE « DÉJÀ ANNULÉE ».
        //
        // Un refus du restaurant fait tomber la commande commerciale, qui publie à
        // son tour « commande annulée » — laquelle revient ici demander
        // l'annulation d'un ticket déjà refusé. Traiter ce retour comme une erreur
        // faisait journaliser « le ticket n'a PAS été arrêté, prévenez le
        // restaurant » à CHAQUE refus, c'est-à-dire sur le chemin nominal du §24.
        //
        // Dans les deux cas, l'état demandé est atteint : la cuisine ne prépare
        // rien. C'est la définition d'une opération idempotente.
        if (Status is FoodOrderStatus.Cancelled or FoodOrderStatus.Rejected)
        {
            return Result.Success();
        }

        if (Status is FoodOrderStatus.PickedUp or FoodOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food.order.not_cancellable", "Cette commande ne peut plus être annulée."));
        }

        var etaitEnCuisine = Status is FoodOrderStatus.Accepted
            or FoodOrderStatus.Preparing or FoodOrderStatus.ReadyForPickup;

        Status = FoodOrderStatus.Cancelled;

        // Le ticket dérive déjà en « annulé » ; l'événement porte l'information qui
        // COMPTE pour le restaurant : y avait-il des denrées engagées ?
        Raise(new FoodOrderCancelledDomainEvent(
            Id.Value, Origin, OrderId, RestaurantId, reason, etaitEnCuisine));

        return Result.Success();
    }

    // ── Mécanique interne ───────────────────────────────────────────────────

    /// <summary>
    /// La cuisine peut-elle travailler sur cette commande ?
    ///
    /// Un ticket ne s'ouvre qu'après acceptation, et se ferme dès l'enlèvement.
    /// Entre les deux, on accepte les gestes dans n'importe quel ordre : la
    /// cuisine n'est pas une machine à états, c'est un plan de travail.
    /// </summary>
    private Result EnsureKitchenOpen()
    {
        if (Status is FoodOrderStatus.Accepted or FoodOrderStatus.Preparing or FoodOrderStatus.ReadyForPickup)
        {
            return Result.Success();
        }

        return Result.Failure(Error.Conflict(
            "food.order.kitchen_closed",
            Status == FoodOrderStatus.PendingRestaurantAcceptance
                ? "Cette commande n'a pas encore été acceptée."
                : "Cette commande n'est plus en cuisine."));
    }

    private Result OnKitchenItem(Guid itemId, Func<FoodOrderItem, bool> action, DateTime nowUtc)
    {
        var garde = EnsureKitchenOpen();
        if (garde.IsFailure)
        {
            return garde;
        }

        var ligne = _items.FirstOrDefault(i => i.Id == itemId);
        if (ligne is null)
        {
            return Result.Failure(Error.NotFound("food.order.item_not_found", "Ligne introuvable sur cette commande."));
        }

        action(ligne);

        EnterPreparation(nowUtc);
        SettleReadiness(nowUtc);

        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE PASSE EN PRÉPARATION DÈS QUE LA CUISINE TOUCHE UNE LIGNE.
    ///
    /// C'EST ICI QUE LE §20 EST TENU — « interdire Ready sans passage par le
    /// workflow de préparation » — et il est tenu PAR CONSTRUCTION plutôt que par
    /// un refus.
    ///
    /// La lecture stricte aurait été d'exiger un appui sur « commencer » avant
    /// « prêt ». Elle punit le barman qui sert un Coca en cinq secondes, et se
    /// solde par deux appuis machinaux qui ne mesurent plus rien.
    ///
    /// Ici, marquer une ligne prête depuis « à préparer » fait passer la commande
    /// par « en préparation » avant « prête », dans le même geste. L'étape n'est
    /// jamais sautée, et personne n'a rien à subir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private void EnterPreparation(DateTime nowUtc)
    {
        if (_items.All(i => i.Status == KitchenItemStatus.Pending))
        {
            return;
        }

        StartedAtUtc ??= nowUtc;

        if (Status == FoodOrderStatus.Accepted)
        {
            Status = FoodOrderStatus.Preparing;
            Raise(new FoodOrderPreparationStartedDomainEvent(Id.Value, Origin, OrderId, RestaurantId));
        }
    }

    /// <summary>
    /// « La commande globale est prête si TOUTES les stations sont READY » (§13).
    ///
    /// RÉVERSIBLE, ET C'EST VOULU. Si une ligne repasse en préparation — un plat
    /// renversé, une erreur de saisie —, la commande quitte « prête ». Sans cela,
    /// un livreur resterait appelé sur une commande qui ne l'est plus.
    /// </summary>
    private void SettleReadiness(DateTime nowUtc)
    {
        var toutEstPret = _items.Count > 0 && _items.All(i => i.Status == KitchenItemStatus.Ready);

        if (toutEstPret && Status == FoodOrderStatus.Preparing)
        {
            Status = FoodOrderStatus.ReadyForPickup;
            ReadyAtUtc = nowUtc;

            // C'EST CET ÉVÉNEMENT QUI APPELLE UN LIVREUR. Le §24 le place au
            // centre du flux : ReadyForPickup → HBA Delivery → HBA Driver.
            Raise(new FoodOrderReadyForPickupDomainEvent(
                Id.Value, Origin, OrderId, RestaurantId, ReadyAtUtc.Value));
            return;
        }

        if (!toutEstPret && Status == FoodOrderStatus.ReadyForPickup)
        {
            Status = FoodOrderStatus.Preparing;
            ReadyAtUtc = null;
        }
    }
}

/// <summary>Accès aux commandes Food.</summary>
public interface IFoodOrderRepository
{
    Task<FoodOrder?> GetByIdAsync(FoodOrderId id, CancellationToken cancellationToken = default);

    /// <summary>Par la commande commerciale. Sert l'idempotence de la réception.</summary>
    /// <summary>
    /// Le ticket d'une commande, DANS SON UNIVERS.
    ///
    /// L'ORIGINE FAIT PARTIE DE LA CLÉ. `OrderId` seul ne désigne rien : deux
    /// ponts écrivent dans ce champ avec des identifiants d'agrégats distincts
    /// (voir <see cref="FoodOrderOrigin"/>). Une recherche sans l'origine pouvait
    /// rendre le ticket de l'autre univers — et la garde d'idempotence de
    /// `ReceiveFoodOrderCommand` s'appuie sur cette lecture.
    /// </summary>
    Task<FoodOrder?> GetByOrderIdAsync(
        FoodOrderOrigin origin, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Le tableau de cuisine : ce qui est encore en jeu, du plus prioritaire au plus ancien.</summary>
    Task<IReadOnlyList<FoodOrder>> ListActiveAsync(
        Guid restaurantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FoodOrder>> ListByStatusAsync(
        Guid restaurantId, FoodOrderStatus status, int take, CancellationToken cancellationToken = default);

    /// <summary>Commandes en cours, pour la saturation (§14 : <c>MaximumActiveOrders</c>).</summary>
    Task<int> CountActiveAsync(Guid restaurantId, CancellationToken cancellationToken = default);

    Task AddAsync(FoodOrder order, CancellationToken cancellationToken = default);
}
