using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Orders.Domain.Orders.SellerOrders.Events;

// Même alias que `PlaceOrderCommandHandler` et `OrderLifecycleCommands` : sous
// l'espace englobant `HBA.Orders.…`, « Order » se résout mal, et le compilateur
// ne le signale qu'à la ligne suivante, sur une conversion impossible.
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>
/// La part d'UN vendeur dans une commande, avec l'état de ce que CE vendeur doit
/// faire. Agrégat racine : possède ses lignes.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CET AGRÉGAT EXISTE (ISSUE-027, décision D29).
///
/// IL NE REMPLACE PAS LE CYCLE DE VIE D'<see cref="OrderAggregate"/>, IL S'Y AJOUTE.
///
/// La saga de la commande — Pending → AwaitingPayment → Paid → Confirmed →
/// Delivered, plus Cancelled, Failed et UnderReview — est branchée sur le
/// paiement, la réservation de stock, la création de course et le règlement des
/// vendeurs. Elle reste seule maîtresse de tout cela. Ce qui est construit ici
/// est une VUE PAR VENDEUR de ce que le vendeur a à faire, POSÉE AU-DESSUS.
///
/// Unifier les deux serait la faute la plus coûteuse possible ici : « confirmée »
/// à l'échelle de la commande veut dire « le paiement est encaissé », et une
/// commande à deux vendeurs dont un seul a accepté n'est ni payée à moitié, ni
/// confirmée à moitié. Aucun compilateur ne signalerait la confusion ; le
/// paiement, la libération de stock et le calcul des gains se tromperaient en
/// silence.
///
/// CE QUE SON ABSENCE COÛTAIT.
///
/// Cinq permissions existaient, étaient attribuées à `ORDER_MANAGER`, et ne
/// gardaient AUCUNE route (ISSUE-026) : `ORDER_CONFIRM`, `ORDER_REJECT`,
/// `ORDER_MARK_PREPARING`, `ORDER_MARK_READY`, `ORDER_CANCEL`. Le rôle promettait
/// une autorité qu'il n'exerçait pas, et le parcours vendeur s'arrêtait à la
/// RÉCEPTION de la commande — il n'y avait rien à confirmer, parce qu'il n'y avait
/// aucun objet à faire changer d'état. `OrderingModuleApi` rendait d'ailleurs
/// `SellerOrderId: null` en dur, ce qui était la trace la plus visible du manque.
///
/// IL NAÎT À LA CONFIRMATION DE LA COMMANDE, JAMAIS AU PASSAGE DE COMMANDE.
///
/// Avant le paiement il n'y a rien qu'un vendeur puisse faire, et lui montrer une
/// commande non payée l'inviterait à préparer un colis pour un paiement qui
/// échouera. C'est le même raisonnement qui fait que `OrderConfirmed` — et non
/// `OrderPlaced` — est l'événement qui prévient les vendeurs.
///
/// UNE COMMANDE DE REPAS N'EN PRODUIT AUCUN, ET CE N'EST PAS UN OUBLI.
///
/// Voir <see cref="SplitFrom"/> : le découpage réutilise le filtre exact de
/// `Order.BuildSellerShares`, qui écarte les lignes de repas. Un plat n'a pas de
/// vendeur au sens de la marketplace ; en fabriquer un produirait une commande
/// vendeur attribuée au vendeur « 00000000-… », que personne n'ouvrirait jamais.
///
/// LE VERROU OPTIMISTE EST RÉELLEMENT ÉVALUÉ ICI — VÉRIFIÉ, PAS SUPPOSÉ.
///
/// L'encadré de `InventoryItem.StockVersion` décrit le piège : une mutation qui
/// n'écrit que des lignes ENFANTS n'émet aucun `UPDATE` sur le parent, donc le
/// jeton `xmin` n'entre dans aucune clause `WHERE` et le verrou est inerte. Cet
/// agrégat n'est PAS concerné, et pour une raison structurelle et non par chance :
/// ses lignes sont FIGÉES à la création et aucune transition n'y touche. Chacune
/// des six écrit le statut et un horodatage sur la ligne parente, donc chacune
/// émet un `UPDATE seller_orders`. Aucun compteur à la `StockVersion` n'est
/// nécessaire.
///
/// La règle à tenir si quelqu'un ajoute plus tard une transition qui ne mute
/// qu'une ligne : il faudra ce compteur, ou le verrou redeviendra décoratif.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SellerOrder : AggregateRoot<SellerOrderId>
{
    private readonly List<SellerOrderLine> _lines = new();

    private SellerOrder()
    {
    }

    private SellerOrder(
        SellerOrderId id, Guid orderId, Guid sellerId, Guid buyerId, string currency, DateTime nowUtc)
        : base(id)
    {
        OrderId = orderId;
        SellerId = sellerId;
        BuyerId = buyerId;
        Currency = currency;
        Status = SellerOrderStatus.AwaitingConfirmation;
        CreatedAtUtc = nowUtc;
    }

    /// <summary>La commande dont ceci est une part. Jamais nul.</summary>
    public Guid OrderId { get; private set; }

    public Guid SellerId { get; private set; }

    /// <summary>
    /// L'acheteur, RECOPIÉ depuis la commande.
    ///
    /// Il n'est pas rendu au vendeur (voir `OrderMapper.ToSellerSummary`, qui
    /// retire déjà téléphone et position). Il est là pour que l'événement de refus
    /// dise QUI prévenir sans relire la commande — un message asynchrone qui doit
    /// déclencher un appel synchrone est exactement ce que le découplage évite.
    /// </summary>
    public Guid BuyerId { get; private set; }

    /// <summary>Devise de la commande, recopiée. Un montant sans devise ne se rembourse pas.</summary>
    public string Currency { get; private set; } = default!;

    public SellerOrderStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    // LES HORODATAGES DE TRANSITION.
    //
    // UN CHAMP PAR ÉTAPE, ET NON UN SEUL « DernièreTransitionUtc ».
    //
    // Un champ unique répond « quand a-t-elle bougé pour la dernière fois » ; il
    // ne répond pas « depuis combien de temps ce vendeur laisse-t-il traîner une
    // commande qu'il a acceptée », qui est la seule question que l'exploitation
    // pose vraiment. C'est le même raisonnement que `Order.UnderReviewSinceUtc` :
    // sans la date d'ENTRÉE dans l'état, une file ne se trie pas.
    //
    // Ils restent nuls tant que l'étape n'a pas eu lieu : nul veut dire « ce n'est
    // pas arrivé », pas « on ne sait pas ».
    // ─────────────────────────────────────────────────────────────────────────
    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? PreparingAtUtc { get; private set; }

    public DateTime? ReadyForPickupAtUtc { get; private set; }

    public DateTime? HandedOverAtUtc { get; private set; }

    public DateTime? RefusedAtUtc { get; private set; }

    /// <summary>
    /// Pourquoi cette part ne sera pas honorée. Nul tant qu'elle l'est encore.
    ///
    /// C'EST LA SEULE TRACE DE POURQUOI UNE COMMANDE PAYÉE N'EST PAS HONORÉE.
    ///
    /// Il est OBLIGATOIRE sur le refus comme sur l'annulation, et pour la même
    /// raison que le motif d'annulation d'arbitrage l'est côté commande : la
    /// décision retire quelque chose à un client qui a payé, elle sera relue le
    /// jour où il réclame, et un journal applicatif ne survit pas à trois mois.
    /// </summary>
    public string? RefusalReason { get; private set; }

    public IReadOnlyCollection<SellerOrderLine> Lines => _lines.AsReadOnly();

    /// <summary>Nombre d'articles de cette part (somme des quantités).</summary>
    public int ItemCount => _lines.Sum(l => l.Quantity);

    /// <summary>
    /// Montant PAYÉ pour cette part, remises comprises.
    ///
    /// CE N'EST PAS CE QUE LE VENDEUR TOUCHERA : la commission de la plateforme
    /// se retire plus loin, dans Settlement. Même mise en garde que sur
    /// `OrderSellerShare` — confondre les deux ferait attendre au vendeur un
    /// virement plus élevé que celui qui arrivera.
    ///
    /// ET CE N'EST PAS NON PLUS LE FRAIS DE PORT. Il est porté par la COMMANDE ;
    /// aucune règle ne permet de le répartir entre deux vendeurs, et l'inventer ici
    /// afficherait un montant que rien ne justifie.
    /// </summary>
    public decimal Amount => _lines.Sum(l => l.LineTotal);

    /// <summary>Cette part attend-elle encore un geste du vendeur ?</summary>
    public bool IsOpen => Status is not (SellerOrderStatus.HandedOver
        or SellerOrderStatus.Rejected
        or SellerOrderStatus.Cancelled);

    /// <summary>
    /// Découpe une commande CONFIRMÉE en une part par vendeur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE FILTRE N'EST PAS RECOPIÉ : IL EST PARTAGÉ AVEC `BuildSellerShares`.
    ///
    /// `Order.SellerLineGroups()` est la SEULE définition de « les lignes qui
    /// appartiennent à un vendeur ». La répartition envoyée dans
    /// `OrderConfirmed` et le découpage fait ici la lisent tous les deux. Écrire
    /// ici un second `Where(line => line.Kind == Goods)` aurait marché le premier
    /// jour et divergé au premier ajout d'une troisième nature de ligne — avec,
    /// comme symptôme, une notification vendeur sans commande vendeur en face,
    /// ou l'inverse.
    ///
    /// UNE COMMANDE DE REPAS REND UNE LISTE VIDE, PAS UNE ERREUR.
    ///
    /// C'est le cas NORMAL pour la restauration, pas un échec : le restaurant
    /// travaille sur un ticket de cuisine, dans food-service, avec son propre
    /// cycle d'acceptation. L'appelant doit pouvoir enchaîner sans se poser la
    /// question.
    ///
    /// CE N'EST PAS ICI QUE L'IDEMPOTENCE SE JOUE.
    ///
    /// Cette méthode fabrique ; elle ne sait pas ce qui existe déjà en base. La
    /// confirmation peut être rejouée — Kafka livre au moins une fois — et deux
    /// parts pour le même (commande, vendeur) doubleraient la vue du vendeur. La
    /// garde est double et vit ailleurs : une relecture applicative dans
    /// `ConfirmOrderPaymentCommandHandler`, et surtout l'index unique
    /// `(OrderId, SellerId)` posé par la migration — seul lui ferme la course
    /// entre deux rejeux SIMULTANÉS, qui lisent tous deux « rien » avant que
    /// l'un ait écrit.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static Result<IReadOnlyList<SellerOrder>> SplitFrom(OrderAggregate order, DateTime nowUtc)
    {
        // ON EXIGE « CONFIRMÉE », ET C'EST L'INVARIANT DE NAISSANCE.
        //
        // Une part créée sur une commande simplement `Paid` — ou pire, `Pending` —
        // apparaîtrait dans le carnet d'un vendeur avant que l'encaissement soit
        // acquis. Il préparerait un colis pour un paiement qui peut encore
        // échouer, et le seul moyen de le lui reprendre serait de supprimer des
        // lignes, ce que ce dépôt ne fait nulle part.
        if (order.Status != OrderStatus.Confirmed)
        {
            return Error.Conflict(
                "ordering.seller_order.order_not_confirmed",
                "Une commande vendeur ne se crée qu'à la confirmation de la commande.");
        }

        var parts = new List<SellerOrder>();

        foreach (var groupe in order.SellerLineGroups())
        {
            var part = new SellerOrder(
                SellerOrderId.New(), order.Id.Value, groupe.Key, order.BuyerId, order.Currency, nowUtc);

            foreach (var ligne in groupe)
            {
                part._lines.Add(new SellerOrderLine(
                    Guid.NewGuid(),
                    ligne.Id,
                    ligne.ProductId,
                    ligne.Sku,
                    ligne.ShipFromLocationId,
                    ligne.Quantity,
                    ligne.FinalUnitPrice));
            }

            parts.Add(part);
        }

        return Result.Success<IReadOnlyList<SellerOrder>>(parts);
    }

    /// <summary>Le vendeur s'engage à honorer sa part.</summary>
    public Result Confirm(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.AwaitingConfirmation)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Cette commande vendeur n'attend plus de confirmation."));
        }

        Status = SellerOrderStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Le vendeur REFUSE sa part, avant de s'être engagé.
    /// </summary>
    /// <remarks>
    /// LE MOTIF EST OBLIGATOIRE. Voir <see cref="RefusalReason"/> : c'est la
    /// seule trace de pourquoi une commande PAYÉE ne sera pas honorée.
    ///
    /// CE REFUS NE REMBOURSE PERSONNE AUJOURD'HUI. Il lève
    /// <see cref="SellerOrderRefusedDomainEvent"/>, qui n'a aucun consommateur —
    /// l'encadré de cet événement dit exactement ce qui manque et où. Une lacune
    /// nommée vaut mieux qu'un silence.
    /// </remarks>
    public Result Reject(string reason, DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.AwaitingConfirmation)
        {
            // MESSAGE QUI DÉSIGNE L'AUTRE GESTE, PARCE QUE L'AUTRE GESTE EXISTE.
            //
            // Un vendeur qui a confirmé puis découvre une rupture n'est pas dans
            // une impasse : il annule. Répondre « transition invalide » sans le
            // dire l'enverrait au support pour un geste qui est à un clic.
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_engaged",
                "Cette commande a déjà été confirmée : elle ne se refuse plus, elle s'annule."));
        }

        return Refuser(SellerOrderStatus.Rejected, "Rejected", reason, nowUtc);
    }

    /// <summary>Le colis se monte.</summary>
    public Result MarkPreparing(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur confirmée peut passer en préparation."));
        }

        Status = SellerOrderStatus.Preparing;
        PreparingAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Le colis attend le livreur.
    /// </summary>
    /// <remarks>
    /// ON N'ACCEPTE PAS LE SAUT « CONFIRMÉE → PRÊTE », ET C'EST DISCUTÉ.
    ///
    /// Le laisser passer coûterait une ligne et arrangerait le vendeur d'un seul
    /// article, qui emballe en dix secondes. Mais alors `Preparing` deviendrait
    /// FACULTATIF, donc absent de la moitié des commandes — et un état qu'on peut
    /// sauter ne dit plus rien à celui qui le lit. Or c'est précisément ce que
    /// l'exploitation regarde pour distinguer « accepté et en cours » de
    /// « accepté et oublié ».
    ///
    /// Le coût est réel et assumé : un clic de plus. Si l'usage montre qu'il est
    /// systématiquement fait juste avant celui-ci, la bonne réponse sera de le
    /// faire poser par l'interface, pas d'ouvrir la transition ici.
    /// </remarks>
    public Result MarkReadyForPickup(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.Preparing)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur en préparation peut être déclarée prête."));
        }

        Status = SellerOrderStatus.ReadyForPickup;
        ReadyForPickupAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Le colis a quitté le vendeur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// AUCUNE ROUTE NE MÈNE ICI AUJOURD'HUI, ET C'EST VOULU PLUTÔT QU'OUBLIÉ.
    ///
    /// Les cinq permissions du §10 s'arrêtent à `ORDER_MARK_READY` : le catalogue
    /// n'a pas de `ORDER_MARK_HANDED_OVER`, parce que la remise n'est PAS une
    /// déclaration du vendeur. C'est un fait constaté par le livreur, et c'est
    /// delivery-service qui le connaît — laisser le vendeur le déclarer
    /// rouvrirait, à l'échelle de la part, la faille que les trois routes de
    /// saga retirées d'`OrderEndpoints` avaient à l'échelle de la commande : une
    /// étape de logistique prononcée par la partie qui y a intérêt.
    ///
    /// La transition existe donc pour que l'état terminal soit atteignable le
    /// jour où l'enlèvement coursier sera branché ici. En attendant, une part
    /// reste `ReadyForPickup` pour toujours, et ce n'est pas un blocage : la
    /// commande, elle, poursuit son cycle et se clôt par `OrderDelivered`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Result MarkHandedOver(DateTime nowUtc)
    {
        if (Status != SellerOrderStatus.ReadyForPickup)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.invalid_transition",
                "Seule une commande vendeur prête peut être remise au livreur."));
        }

        Status = SellerOrderStatus.HandedOver;
        HandedOverAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Le vendeur se dédit APRÈS s'être engagé : rupture découverte à
    /// l'emballage, casse, article introuvable.
    /// </summary>
    /// <remarks>
    /// DISTINCTE DE <see cref="Reject"/> POUR LE VENDEUR, IDENTIQUE POUR LA SUITE.
    ///
    /// Deux permissions les séparent — `ORDER_REJECT` est normale, `ORDER_CANCEL`
    /// est SENSIBLE, parce que se dédire après avoir fait attendre le client
    /// n'est pas le même geste que refuser tout de suite. Mais la conséquence en
    /// aval est la même au mot près, d'où un seul événement : voir
    /// <see cref="SellerOrderRefusedDomainEvent"/>.
    ///
    /// PAS DEPUIS « REMISE AU LIVREUR ». Le colis est parti ; le reprendre
    /// n'est plus une annulation, c'est un RETOUR, avec ses règles propres et son
    /// service. C'est le même invariant que `Order.Cancel` tient à l'échelle de
    /// la commande : une vente conclue ne s'annule pas, elle se retourne.
    /// </remarks>
    public Result Cancel(string reason, DateTime nowUtc)
    {
        if (Status == SellerOrderStatus.AwaitingConfirmation)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.not_yet_engaged",
                "Cette commande n'a pas encore été confirmée : elle ne s'annule pas, elle se refuse."));
        }

        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_closed",
                "Cette commande vendeur n'est plus annulable dans cet état."));
        }

        return Refuser(SellerOrderStatus.Cancelled, "Cancelled", reason, nowUtc);
    }

    /// <summary>
    /// La COMMANDE ENTIÈRE a été annulée : la part du vendeur tombe avec elle.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ELLE NE LÈVE AUCUN ÉVÉNEMENT, ET C'EST TOUT SON INTÉRÊT.
    ///
    /// `OrderCancelled` a déjà été publié par la commande, et c'est LUI que
    /// financial-service consomme pour rembourser — la totalité, puisque la
    /// commande entière tombe. Lever ici un refus vendeur en plus ferait, le jour
    /// où ce refus aura enfin un consommateur, rembourser une seconde fois la
    /// part du vendeur sur une commande déjà intégralement remboursée.
    ///
    /// SANS CETTE TRANSITION, LE VENDEUR PRÉPARE UN COLIS REMBOURSÉ.
    ///
    /// Le seul chemin qui annule une commande APRÈS confirmation est
    /// `CancelAfterReview` — l'exploitation tranche en faveur du retour. Les
    /// parts vendeur, elles, resteraient `Confirmed` ou `Preparing` dans leur
    /// carnet, sans un mot, pour une vente dont l'argent est déjà reparti.
    ///
    /// UNE PART DÉJÀ CLOSE N'EST PAS ROUVERTE. Un refus vendeur antérieur garde
    /// son motif : c'est l'histoire, et l'écraser par « commande annulée » ferait
    /// perdre la CAUSE au profit de la conséquence — la même erreur que
    /// `ReviewReason` évite côté commande.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public Result CancelWithOrder(string reason, DateTime nowUtc)
    {
        if (!IsOpen)
        {
            return Result.Failure(Error.Conflict(
                "ordering.seller_order.already_closed",
                "Cette commande vendeur est déjà close."));
        }

        Status = SellerOrderStatus.Cancelled;
        RefusedAtUtc = nowUtc;
        RefusalReason = Motif(reason, "Commande annulée.");
        return Result.Success();
    }

    /// <summary>Le corps commun aux deux refus : même écriture, même événement.</summary>
    private Result Refuser(SellerOrderStatus statut, string issue, string reason, DateTime nowUtc)
    {
        // VALIDATION, PAS CONFLIT : un motif vide est une requête mal formée,
        // pas un état incompatible. Les deux se traduisent en HTTP différemment
        // (400 contre 409), et un client qui ne peut pas les distinguer réessaie
        // là où il devrait corriger sa saisie.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "ordering.seller_order.reason_required",
                "Un motif est obligatoire : c'est la seule trace de pourquoi cette commande payée ne sera pas honorée."));
        }

        var motif = Motif(reason, string.Empty);

        Status = statut;
        RefusedAtUtc = nowUtc;
        RefusalReason = motif;

        Raise(new SellerOrderRefusedDomainEvent(
            Id.Value,
            OrderId,
            BuyerId,
            SellerId,
            Currency,
            issue,
            motif,
            Amount,
            _lines
                .Select(l => new SellerOrderRefusedLine(
                    l.OrderLineId, l.ProductId, l.Sku, l.ShipFromLocationId, l.Quantity, l.LineTotal))
                .ToList()));

        return Result.Success();
    }

    /// <summary>Borne le motif à ce que la colonne accepte : tronquer vaut mieux que perdre.</summary>
    private static string Motif(string reason, string defaut)
    {
        var propre = string.IsNullOrWhiteSpace(reason) ? defaut : reason.Trim();
        return propre.Length > 500 ? propre[..500] : propre;
    }
}
