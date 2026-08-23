using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;
using HBA.Orders.Domain.Orders.Events;

namespace HBA.Orders.Domain.Orders;

/// <summary>
/// Commande multi-vendeur. Fige le prix de chaque ligne (prix de base +
/// réductions par financeur) au moment de l'achat. Son cycle de vie est piloté
/// par un Saga d'orchestration : réservation du stock, paiement, confirmation,
/// avec compensations. Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = new();
    private readonly List<OrderReturnSettlement> _returnSettlements = new();

    private Order()
    {
    }

    private Order(OrderId id, Guid buyerId, Guid cartId, string currency, string? promotionCode)
        : base(id)
    {
        BuyerId = buyerId;
        CartId = cartId;
        Currency = currency;
        PromotionCode = promotionCode;
        Status = OrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }
    public Guid CartId { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>
    /// Code promo appliqué au panier, FIGÉ au moment de la commande. Null si aucun.
    ///
    /// La commande doit s'en souvenir : le panier est clôturé juste après le checkout,
    /// et c'est seulement à la CONFIRMATION (paiement encaissé) que la promotion est
    /// réellement décomptée. Sans ce snapshot, le module Pricing recevrait un événement
    /// « commande confirmée » sans savoir quel coupon consommer — et le coupon resterait
    /// éternellement disponible. C'est très exactement le bug qu'on corrige.
    /// </summary>
    public string? PromotionCode { get; private set; }

    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal TotalSellerDiscount { get; private set; }
    public decimal TotalPlatformDiscount { get; private set; }

    /// <summary>Frais de livraison encaissés par la plateforme (forfait choisi au checkout).</summary>
    public decimal ShippingFee { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DEVIS DE COURSE QUI A FIXÉ CES FRAIS. Restauration seulement.
    ///
    /// SANS LUI, LE CLIENT ET LA PLATEFORME N'ACHÈTENT PAS LA MÊME CHOSE.
    ///
    /// Les frais d'un repas sont chiffrés au checkout, à la distance réelle. La
    /// course, elle, n'est créée que lorsque le sac est prêt — vingt à quarante
    /// minutes plus tard. Redemander un devis à ce moment produit un SECOND prix,
    /// qui peut différer du premier : grille tarifaire modifiée entre-temps, zone
    /// redécoupée, version de tarification incrémentée.
    ///
    /// Le client aurait alors payé un montant, et la plateforme en aurait acheté
    /// un autre. C'est précisément l'écart que le chiffrage au checkout devait
    /// supprimer, simplement déplacé de « forfait contre réel » à « réel d'avant
    /// contre réel d'après ».
    ///
    /// Figer l'identifiant du devis règle les deux à la fois : la course est créée
    /// AU PRIX DÉJÀ PAYÉ, et le devis orphelin créé au checkout devient celui qui
    /// sert.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? DeliveryQuoteId { get; private set; }

    public decimal GrandTotal { get; private set; }

    /// <summary>Identifiant du paiement capture, fige pour les retours/remboursements.</summary>
    public Guid? PaymentId { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Pourquoi la commande a été mise en ARBITRAGE. Nul tant qu'elle ne l'a
    /// jamais été.
    ///
    /// DISTINCT DE <see cref="CancellationReason"/>, ET IL DOIT LE RESTER.
    ///
    /// Une commande en arbitrage n'est PAS annulée : elle est payée, encaissée,
    /// son stock est décrémenté, et l'exploitation va décider si on la relance ou
    /// si on la retourne. Réutiliser le champ d'annulation ferait afficher un
    /// motif d'annulation sur une vente encore vivante — et, le jour où
    /// l'arbitrage conclut à un remboursement, écraserait la cause d'origine par
    /// la décision.
    /// </summary>
    public string? ReviewReason { get; private set; }

    /// <summary>
    /// Depuis quand elle attend une décision humaine.
    ///
    /// C'EST LE TRI DE LA FILE D'ARBITRAGE. Sans cette date, la console ne
    /// peut ordonner les dossiers que par date de commande — et une commande
    /// bloquée depuis trois jours passerait derrière une bloquée depuis dix
    /// minutes.
    /// </summary>
    public DateTime? UnderReviewSinceUtc { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────────
    // Adresse de livraison FIGÉE au moment de la commande. Copie, pas de FK vers
    // Identity : si l'acheteur modifie ou supprime son adresse ensuite, la commande
    // doit continuer de dire où le colis a été envoyé.
    //
    // On fige le CODE de commune, pas son libellé. Les codes ne changent jamais (c'est
    // le contrat de BeninGeography), alors qu'un libellé peut être corrigé — et une
    // correction d'orthographe ne doit pas réécrire l'historique.
    // ─────────────────────────────────────────────────────────────────────────────
    public string? ShipToLabel { get; private set; }
    public string? ShipToRecipient { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCommuneCode { get; private set; }
    public string? ShipToQuartier { get; private set; }
    public string? ShipToLandmark { get; private set; }
    public string? ShipToLine1 { get; private set; }
    public string? ShipToCountryCode { get; private set; }

    /// <summary>
    /// Position figée, quand l'acheteur en avait une. FACULTATIVE : elle complète le
    /// repère, elle ne le remplace pas — et une commande sans position se livre
    /// exactement comme avant.
    ///
    /// Figée elle aussi : si l'acheteur déplace ensuite le point de son adresse, la
    /// commande doit continuer de dire où le colis a été envoyé.
    /// </summary>
    public double? ShipToLatitude { get; private set; }

    public double? ShipToLongitude { get; private set; }

    /// <summary>La commande porte-t-elle un point ouvrable dans une carto ?</summary>
    public bool HasShipToCoordinates => ShipToLatitude is not null && ShipToLongitude is not null;

    /// <summary>Libellé de la commune de livraison, résolu à l'affichage.</summary>
    public string ShipToCommuneName => BeninGeography.CommuneName(ShipToCommuneCode);

    /// <summary>
    /// La commande porte-t-elle une adresse exploitable ?
    ///
    /// Elle s'appuie sur la COMMUNE et le REPÈRE — pas sur la rue, qui est facultative
    /// au Bénin. Une commande dont on ne connaît que « rue X » n'est pas livrable.
    /// </summary>
    public bool HasShippingAddress =>
        !string.IsNullOrWhiteSpace(ShipToCommuneCode) && !string.IsNullOrWhiteSpace(ShipToLandmark);

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    /// <summary>
    /// Ce que les dossiers de retour ont définitivement retiré à cette commande.
    /// Voir <see cref="OrderReturnSettlement"/> pour la raison d'être de cette
    /// collection — c'est le volet order-service d'ISSUE-014.
    /// </summary>
    public IReadOnlyCollection<OrderReturnSettlement> ReturnSettlements => _returnSettlements.AsReadOnly();

    /// <summary>
    /// Ce qui a DÉJÀ été rendu au client sur cette commande, tous dossiers de
    /// retour confondus.
    ///
    /// <para>
    /// Calculée, jamais stockée. Une colonne cumulative aurait exigé sa propre
    /// idempotence — et un seul message compté deux fois aurait durablement fermé
    /// le plafond de remboursement du client, sans trace de la cause. La somme des
    /// dossiers, elle, se reconstruit à l'identique après n'importe quel rejeu.
    /// </para>
    /// </summary>
    public decimal RefundedAmount => _returnSettlements.Sum(s => s.RefundedAmount);

    /// <summary>
    /// Combien d'exemplaires de cette ligne sont DÉJÀ revenus, tous dossiers
    /// confondus. Borné par la quantité commandée : au-delà, la valeur ne
    /// signifierait plus rien pour l'appelant, qui en déduit ce qui reste
    /// retournable.
    /// </summary>
    public int ReturnedQuantityFor(Guid orderItemId)
    {
        var reprise = _returnSettlements.Sum(s => s.QuantityFor(orderItemId));
        var ligne = _lines.FirstOrDefault(l => l.Id == orderItemId);
        return ligne is null ? reprise : Math.Min(reprise, ligne.Quantity);
    }

    /// <summary>
    /// Enregistre ce qu'un dossier de retour a rendu et repris.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// IDEMPOTENTE PAR CONSTRUCTION, ET PAS SEULEMENT PAR L'INBOX.
    ///
    /// Les valeurs reçues sont CUMULÉES POUR LE DOSSIER : on les POSE — au maximum
    /// vu — au lieu de les additionner. Le même message rejoué n'impute donc rien
    /// de plus, et un message ancien remis après un récent ne fait pas reculer le
    /// compteur. L'inbox reste la garde de premier rang ; ceci est ce qui reste
    /// vrai le jour où elle manque.
    ///
    /// UNE LIGNE INCONNUE EST IGNORÉE, PAS REFUSÉE.
    ///
    /// Elle ne peut pas être rapprochée, donc elle n'imputera jamais rien. Refuser
    /// le message entier ferait perdre le MONTANT remboursé — le plafond
    /// resterait ouvert — pour une ligne qui, au pire, correspond à une commande
    /// scindée dont nous ne portons qu'une part.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result RecordReturnSettlement(
        Guid returnRequestId,
        decimal totalRefunded,
        IReadOnlyCollection<ReturnSettlementLineDraft> lines,
        DateTime nowUtc)
    {
        if (returnRequestId == Guid.Empty)
        {
            return Result.Failure(Error.Validation(
                "order.return_settlement.identity_required",
                "Le dossier de retour est obligatoire."));
        }

        if (totalRefunded < 0m)
        {
            return Result.Failure(Error.Validation(
                "order.return_settlement.amount_invalid",
                "Un montant rembourse ne peut pas etre negatif."));
        }

        var connues = lines
            .Where(l => _lines.Any(ligne => ligne.Id == l.OrderItemId))
            .ToList();

        var dossier = _returnSettlements.FirstOrDefault(s => s.ReturnRequestId == returnRequestId);
        if (dossier is null)
        {
            dossier = new OrderReturnSettlement(Guid.NewGuid(), returnRequestId, nowUtc);
            _returnSettlements.Add(dossier);
        }

        dossier.Retenir(totalRefunded, connues, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// La nature de la commande. Une commande vide n'existe pas : `Create` en exige
    /// au moins une ligne, donc cette propriété n'est jamais indéterminée.
    ///
    /// UNE COMMANDE NE MÉLANGE PAS PLATS ET MARCHANDISE — voir `Create`.
    /// </summary>
    public OrderLineKind Kind => _lines.Count == 0 ? OrderLineKind.Goods : _lines[0].Kind;

    /// <summary>
    /// L'établissement qui prépare cette commande, ou <c>null</c> si ce n'est pas
    /// une commande de repas.
    ///
    /// C'EST CE QUE L'ADAPTATEUR VERS FOOD LIT POUR SAVOIR OÙ ENVOYER LE TICKET.
    /// Le déduire de la première ligne serait fragile ; l'invariant d'unicité du
    /// restaurant est posé par `Create`, et cette propriété s'y appuie.
    /// </summary>
    public Guid? RestaurantId => Kind == OrderLineKind.Food && _lines.Count > 0
        ? _lines[0].RestaurantId
        : null;

    public static Result<Order> Create(
        Guid buyerId,
        Guid cartId,
        string currency,
        IEnumerable<OrderLineDraft> drafts,
        string? promotionCode = null)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        var draftList = drafts?.ToList() ?? new List<OrderLineDraft>();
        if (draftList.Count == 0)
        {
            return Error.Validation("ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (draftList.Any(d => d.Quantity <= 0))
        {
            return Error.Validation("ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        // ═════════════════════════════════════════════════════════════════════
        // UNE COMMANDE NE MÉLANGE PAS LES DEUX NATURES.
        //
        // Le panier l'interdit déjà, mais `Create` est appelable sans lui — et
        // c'est ICI que l'invariant compte : une commande mixte devrait réserver
        // du stock pour la moitié de ses lignes, être acceptée par une cuisine
        // pour l'autre, et produire deux livraisons aux délais incompatibles.
        // Aucun code en aval ne sait faire cela ; le refuser à la création est la
        // dernière occasion de le dire clairement.
        // ═════════════════════════════════════════════════════════════════════
        if (draftList.Select(d => d.Kind).Distinct().Count() > 1)
        {
            return Error.Validation(
                "ordering.mixed_kinds", "Une commande ne peut pas mêler des plats et des articles.");
        }

        var foodDrafts = draftList.Where(d => d.Kind == OrderLineKind.Food).ToList();

        if (foodDrafts.Count > 0)
        {
            // SANS RESTAURANT NI PLAT, LA COMMANDE EST INENVOYABLE EN CUISINE.
            //
            // Elle serait payée, puis l'adaptateur vers Food n'aurait rien à
            // adresser. L'échec doit se produire avant le paiement.
            if (foodDrafts.Any(d => d.RestaurantId == Guid.Empty || d.MenuItemId == Guid.Empty))
            {
                return Error.Validation(
                    "ordering.food_line_incomplete", "Une ligne de repas doit désigner un restaurant et un plat.");
            }

            // Deux cuisines, ce sont deux temps de préparation et deux collectes :
            // le livreur attendrait la plus lente en laissant refroidir l'autre.
            if (foodDrafts.Select(d => d.RestaurantId).Distinct().Count() > 1)
            {
                return Error.Validation(
                    "ordering.multiple_restaurants", "Une commande ne peut concerner qu'un seul restaurant.");
            }
        }

        var order = new Order(
            OrderId.New(),
            buyerId,
            cartId,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim().ToUpperInvariant());

        foreach (var draft in draftList)
        {
            order._lines.Add(new OrderLine(Guid.NewGuid(), draft));
        }

        order.RecomputeTotals();
        return order;
    }

    /// <summary>Fige l'adresse de livraison choisie (copie intégrale dans la commande).</summary>
    public void SetShippingAddress(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, string? countryCode,
        double? latitude, double? longitude)
    {
        ShipToLabel = Trim(label, 60);
        ShipToRecipient = Trim(recipient, 120);
        ShipToPhone = Trim(phone, 20);
        ShipToCommuneCode = Trim(communeCode, 40);
        ShipToQuartier = Trim(quartier, 120);
        ShipToLandmark = Trim(landmark, 200);
        ShipToLine1 = Trim(line1, 200);
        ShipToCountryCode = Trim(countryCode, 2) ?? BeninGeography.CountryCode;

        // Les deux ou aucune : une latitude seule placerait le point dans le golfe
        // de Guinée, à 400 km de Cotonou.
        ShipToLatitude = latitude is not null && longitude is not null ? latitude : null;
        ShipToLongitude = ShipToLatitude is null ? null : longitude;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    /// <summary>Fixe les frais de livraison (forfait choisi) et recalcule le total.</summary>
    public void SetShippingFee(decimal fee, string? deliveryQuoteId = null)
    {
        ShippingFee = fee < 0m ? 0m : fee;
        DeliveryQuoteId = string.IsNullOrWhiteSpace(deliveryQuoteId) ? null : deliveryQuoteId;
        RecomputeTotals();
    }

    private void RecomputeTotals()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal) + ShippingFee;
    }

    /// <summary>Stock réservé : la commande attend le paiement. Émet OrderPlaced.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != OrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = OrderStatus.AwaitingPayment;
        Raise(new OrderPlacedDomainEvent(Id.Value, BuyerId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid(Guid paymentId)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        if (paymentId == Guid.Empty)
        {
            return Result.Failure(Error.Validation("ordering.payment_required", "Le paiement capture est obligatoire."));
        }

        PaymentId = paymentId;
        Status = OrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>Réservations soldées : la commande est confirmée.</summary>
    public Result Confirm()
    {
        if (Status != OrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(
            Id.Value, BuyerId, Currency, PromotionCode, BuildSellerShares(),
            Kind.ToString(), RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// Répartit la commande entre ses vendeurs : combien d'articles, pour quel montant.
    ///
    /// C'est la commande — et elle seule — qui détient cette vérité : elle connaît ses
    /// lignes, donc leurs vendeurs et leurs prix. La reconstituer ailleurs (dans le
    /// module Notifications, par exemple) obligerait à relire les lignes depuis un
    /// autre schéma, ce que l'architecture modulaire interdit précisément.
    ///
    /// Le montant est la somme des <c>LineTotal</c> du vendeur : le prix FINAL payé,
    /// remises comprises. Ce n'est pas encore ce qu'il touchera — la commission de la
    /// plateforme se retire plus loin, dans Settlement. La notification annonce donc
    /// « voici ce qui a été acheté chez vous », et non « voici ce que vous toucherez ».
    /// Confondre les deux ferait attendre au vendeur un virement plus élevé que celui
    /// qui arrivera.
    /// </summary>
    private List<OrderSellerShare> BuildSellerShares()
        => SellerLineGroups()
            .Select(g => new OrderSellerShare(
                g.Key,
                g.Sum(line => line.Quantity),
                g.Sum(line => line.LineTotal)))
            .ToList();

    /// <summary>
    /// Les lignes qui appartiennent à un VENDEUR, groupées par vendeur.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UNE COMMANDE DE REPAS N'A PAS DE VENDEUR, ET N'EN INVENTE PAS UN.
    ///
    /// Toutes ses lignes portent `SellerId = Guid.Empty`. Sans ce filtre, le
    /// regroupement produirait UNE part attribuée au vendeur « 00000000-… », et
    /// trois consommateurs agiraient dessus : Wallet créditerait les gains d'un
    /// compte inexistant, Sellers incrémenterait ses statistiques de vente, et
    /// Notifications chercherait à prévenir un vendeur qui n'existe pas.
    ///
    /// La rémunération du restaurant est un sujet distinct, qui passera par
    /// Food — pas par la répartition vendeur de la marketplace.
    ///
    /// EXTRAIT DE `BuildSellerShares` POUR QUE LE FILTRE N'EXISTE QU'UNE FOIS
    /// (ISSUE-027).
    ///
    /// `SellerOrder.SplitFrom` doit découper la commande EXACTEMENT selon la même
    /// règle que celle qui décide qui est prévenu à la confirmation. Deux copies
    /// du même `Where` auraient marché le premier jour et divergé au premier
    /// ajout d'une nature de ligne — avec, comme symptôme, une notification
    /// vendeur sans commande vendeur en face, ou l'inverse. Rien d'autre n'a
    /// changé : c'est le même filtre, au même endroit du cycle de vie.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    internal IEnumerable<IGrouping<Guid, OrderLine>> SellerLineGroups()
        => _lines
            .Where(line => line.Kind == OrderLineKind.Goods)
            .GroupBy(line => line.SellerId);

    /// <summary>
    /// Livraison confirmée (toutes les expéditions reçues). Déclenche, en aval,
    /// la libération de l'escrow et le payout vendeur (Saga, étape finale).
    /// </summary>
    public Result MarkDelivered()
    {
        // « EN ARBITRAGE » EST ACCEPTÉ ICI, ET C'EST DÉLIBÉRÉ.
        //
        // Une remise CONSTATÉE prouve que le colis est chez le client : le fait
        // est acquis, il n'est pas déduit. Refuser la clôture parce qu'un dossier
        // d'arbitrage traîne gèlerait l'escrow d'une livraison réellement faite,
        // et le vendeur ne serait pas réglé — très exactement la panne que tout
        // ce chemin existe pour éviter.
        //
        // Le cas n'est pas théorique : la course annulée met la commande en
        // arbitrage, l'exploitation la relance, une SECONDE course aboutit — et
        // le message d'annulation de la première peut arriver après. Sans cette
        // tolérance, la commande resterait en arbitrage alors que le client a
        // déjà son colis.
        if (Status is not (OrderStatus.Confirmed or OrderStatus.UnderReview))
        {
            return Result.Failure(Error.Conflict("ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = OrderStatus.Delivered;
        Raise(new OrderDeliveredDomainEvent(Id.Value, BuyerId));
        return Result.Success();
    }

    /// <summary>Annulation (réservations à libérer en compensation).</summary>
    public Result Cancel(string reason)
    {
        // « EN ARBITRAGE » EST REFUSÉ ICI AU MÊME TITRE QUE « CONFIRMÉE ».
        //
        // Une commande en arbitrage a été payée ET son stock a été décrémenté :
        // c'est une commande confirmée qu'un incident a rendue inexécutable, pas
        // une commande redevenue annulable. L'oublier ouvrirait deux trous d'un
        // coup :
        //
        //   • la route acheteur `POST /api/orders/{id}/cancel` deviendrait un
        //     moyen de contourner l'invariant « une vente conclue ne s'annule
        //     pas, elle se retourne » — il suffirait d'attendre que sa course
        //     soit annulée ;
        //   • `CancelOrderCommandHandler` appellerait `ReleaseReservationAsync`
        //     sur des réservations DÉJÀ SOLDÉES à la confirmation. Libérer ce qui
        //     a été consommé fausse le stock disponible.
        //
        // La sortie d'arbitrage passe par `CancelAfterReview`, qui ne touche pas
        // à Inventory et n'est joignable que depuis la console d'exploitation.
        if (Status is OrderStatus.Confirmed
            or OrderStatus.UnderReview
            or OrderStatus.Cancelled
            or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict("ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RESTAURANT A REFUSÉ — LA COMMANDE TOMBE APRÈS AVOIR ÉTÉ CONFIRMÉE.
    ///
    /// `Cancel` NE SUFFIT PAS, ET SON REFUS EST JUSTE.
    ///
    /// `Cancel` interdit l'annulation d'une commande confirmée, et c'est correct
    /// pour la marchandise : confirmée veut dire payée ET stock réservé, et
    /// revenir dessus est un RETOUR, avec ses règles propres.
    ///
    /// La restauration inverse la chronologie. Le §24 place la décision du
    /// restaurant APRÈS le paiement : le client paie, la commande est confirmée,
    /// ET ALORS la cuisine accepte ou refuse. Un refus n'est pas un incident, c'est
    /// une issue prévue — plus de riz, four en panne, fermeture imprévue.
    ///
    /// Sans cette transition, un refus laisserait une commande confirmée pour un
    /// repas que personne ne préparera, et un client débité sans recours.
    ///
    /// RÉSERVÉE À LA RESTAURATION, DÉLIBÉRÉMENT.
    ///
    /// L'ouvrir à la marchandise donnerait un moyen de contourner `Cancel` et son
    /// invariant, c'est-à-dire d'annuler une vente conclue sans passer par un
    /// retour. Le remboursement, lui, appartient à Payments : cette méthode ne
    /// fait qu'ouvrir le droit, elle ne rend pas l'argent.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result RejectByProvider(string reason)
    {
        if (Kind != OrderLineKind.Food)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_food", "Seule une commande de repas peut être refusée par son prestataire."));
        }

        // DEUX CODES DISTINCTS, ET LA DISTINCTION EST FONCTIONNELLE.
        //
        // L'appelant traite « déjà terminale » comme un REJEU — l'outbox livre au
        // moins une fois — et poursuit vers le remboursement, lui-même idempotent.
        // Avec un code unique, il aurait avalé de la même façon « ce n'est pas une
        // commande de repas », c'est-à-dire une vraie anomalie, ET aurait
        // néanmoins déclenché le remboursement d'une commande de marchandise
        // restée vivante.
        // « DÉJÀ LIVRÉE » SE DISTINGUE DE « DÉJÀ ANNULÉE ».
        //
        // L'appelant absorbe `already_terminal` comme un rejeu, puis poursuit vers
        // le remboursement. Sur une commande LIVRÉE, le paiement est encaissé : il
        // rembourserait un repas déjà mangé. Le cas n'est pas théorique — une
        // lettre morte rejouée à la main, ou un message resté en souffrance
        // pendant que la course aboutissait, suffisent.
        if (Status == OrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_delivered", "La commande a déjà été livrée."));
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_terminal", "La commande n'est plus refusable dans cet état."));
        }

        // AVANT LE PAIEMENT, IL N'Y A RIEN À REFUSER.
        //
        // Le ticket de cuisine n'existe qu'après la confirmation : un refus ne peut
        // donc pas concerner une commande `Pending` ou `AwaitingPayment`. Les
        // autoriser ouvrirait un chemin vicieux — une commande en attente de
        // paiement passée en « annulée » pendant que le prestataire de paiement
        // encaisse encore derrière, produisant une capture orpheline que ce
        // handler considérerait comme « rien à rembourser ».
        if (Status is OrderStatus.Pending or OrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_yet_paid", "Une commande non payée ne se refuse pas, elle s'annule."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new OrderCancelledDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA COMMANDE EST DEVENUE INEXÉCUTABLE — ELLE PASSE EN ARBITRAGE.
    ///
    /// SANS CETTE TRANSITION, LA SAGA N'AVAIT AUCUNE SORTIE DE SECOURS.
    ///
    /// Une commande payée et confirmée qui ne peut plus être livrée restait
    /// `Confirmed` POUR TOUJOURS : ni livraison, ni annulation, ni remboursement,
    /// escrow gelé, stock déjà décrémenté. L'acheteur attendait un colis que
    /// personne n'apportait, et rien dans le système ne le signalait — la panne
    /// la plus coûteuse qui soit, parce qu'elle est silencieuse.
    ///
    /// Deux chemins y menaient, et aucun n'aboutissait :
    ///
    ///   • la COURSE ANNULÉE. `DeliveryCancelledIntegrationEvent` n'avait qu'un
    ///     seul consommateur — le webhook partenaire, interne à delivery-service.
    ///     Rien ne remontait ni à order-service, ni à food-service ;
    ///   • l'EXPÉDITION MULTI-LIEUX, que `CreateDeliveryOnOrderConfirmedHandler`
    ///     refuse à juste titre — une course par lieu ferait clore la commande à
    ///     la première remise — mais il s'arrêtait sur un `return;` après un
    ///     journal.
    ///
    /// ON N'ANNULE PAS, ET ON NE REMBOURSE SURTOUT PAS D'OFFICE.
    ///
    /// Une course annulée est le plus souvent RÉATTRIBUABLE : livreur en panne,
    /// refus après acceptation, erreur de dispatch. Rembourser automatiquement
    /// détruirait des ventes récupérables, et l'argent rendu ne se reprend pas.
    /// La commande sort du « en cours » et entre dans une file où un humain
    /// tranche : relancer (`ResumeAfterReview`) ou retourner
    /// (`CancelAfterReview`).
    ///
    /// MODÈLE SUIVI : `RejectByProvider`. Même situation — une commande
    /// confirmée qui ne peut pas être honorée — et même parti pris : la
    /// transition OUVRE UN DROIT, elle ne rend pas l'argent. Le remboursement
    /// appartient à financial-service, qui consomme `OrderCancelled`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result MarkUnderReview(string reason)
    {
        // « DÉJÀ LIVRÉE » SE DISTINGUE DE « DÉJÀ TERMINALE », comme dans
        // `RejectByProvider`, et pour la même raison.
        //
        // Une commande LIVRÉE a été honorée : la mettre en arbitrage rouvrirait
        // un dossier sur une vente close, et l'exploitation pourrait la
        // rembourser après coup. Le cas arrive — l'annulation d'une PREMIÈRE
        // course peut être livrée après qu'une SECONDE a abouti.
        if (Status == OrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_delivered", "La commande a déjà été livrée."));
        }

        // CODE PROPRE AU REJEU, PARCE QUE L'APPELANT DOIT POUVOIR L'AVALER.
        //
        // L'outbox livre AU MOINS une fois : le même message d'annulation de
        // course reviendra. Deuxième passage sur une commande déjà en arbitrage,
        // ce n'est pas un incident — mais confondre ce cas avec « la commande
        // n'est pas dans un état arbitrable » ferait sortir une alerte pour un
        // dossier correctement ouvert.
        if (Status == OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_under_review", "La commande est déjà en arbitrage."));
        }

        if (Status is OrderStatus.Cancelled or OrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.already_terminal", "La commande n'est plus arbitrable dans cet état."));
        }

        // AVANT LA CONFIRMATION, IL N'Y A RIEN À ARBITRER.
        //
        // Une commande non confirmée n'a ni course, ni stock soldé : elle
        // s'annule normalement, avec libération des réservations. La faire
        // entrer ici la sortirait de son propre chemin de compensation et
        // laisserait des réservations vivantes sur un panier mort.
        if (Status != OrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_confirmed", "Une commande non confirmée ne s'arbitre pas, elle s'annule."));
        }

        Status = OrderStatus.UnderReview;
        ReviewReason = reason;
        UnderReviewSinceUtc = DateTime.UtcNow;
        Raise(new OrderUnderReviewDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché : la commande REPART. Elle redevient une
    /// commande confirmée ordinaire.
    /// </summary>
    /// <remarks>
    /// ON NE RELÈVE PAS `OrderConfirmedDomainEvent`, ET C'EST CAPITAL.
    ///
    /// Sept consommateurs écoutent la confirmation : ouverture du ticket de
    /// cuisine, notification des vendeurs, décompte du coupon promo,
    /// comptabilisation des gains, création de la course… Les rejouer ferait
    /// préparer le repas une seconde fois, brûlerait un second coupon et
    /// créditerait deux fois le vendeur. Reprendre une commande n'est pas la
    /// confirmer à nouveau : c'est lever une suspension.
    ///
    /// La nouvelle course, elle, est demandée EXPLICITEMENT par le composition
    /// root après cette transition — un geste voulu, pas un effet de bord.
    /// </remarks>
    public Result ResumeAfterReview()
    {
        if (Status != OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = OrderStatus.Confirmed;

        // ON EFFACE LA DATE, PAS LE MOTIF.
        //
        // La date dit « ce dossier attend depuis… » : la garder ferait
        // réapparaître une commande relancée en tête de la file d'arbitrage. Le
        // motif, lui, est de l'HISTOIRE — c'est ce qui permet de comprendre, une
        // semaine plus tard, pourquoi cette commande a mis trois jours à partir.
        UnderReviewSinceUtc = null;

        Raise(new OrderResumedAfterReviewDomainEvent(Id.Value, BuyerId, ReviewReason ?? string.Empty));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché dans l'autre sens : la vente est retournée, et
    /// l'acheteur sera remboursé.
    /// </summary>
    /// <remarks>
    /// LA SEULE SORTIE VERS « ANNULÉE » DEPUIS L'ARBITRAGE, ET ELLE EST
    /// SÉPARÉE DE `Cancel` À DESSEIN.
    ///
    /// `Cancel` libère les réservations de stock (`ReleaseReservationAsync`) :
    /// c'est correct AVANT la confirmation, où rien n'a encore été décrémenté.
    /// Ici, le stock a été soldé par `ConfirmReservationAsync` — il n'y a plus
    /// de réservation à rendre, il y a de la marchandise à remettre en rayon, ce
    /// qui est un geste d'exploitation dans Inventory et non une compensation de
    /// saga. Appeler la libération par symétrie ferait croire à un travail qui
    /// n'existe pas, et fausserait le disponible.
    ///
    /// CETTE MÉTHODE NE REMBOURSE PAS. Elle publie `OrderCancelled` ;
    /// financial-service rembourse en le consommant. order-service annonce un
    /// fait, il n'ordonne pas un virement.
    /// </remarks>
    public Result CancelAfterReview(string reason)
    {
        if (Status != OrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UnderReviewSinceUtc = null;
        Raise(new OrderCancelledDomainEvent(Id.Value, BuyerId, reason));
        return Result.Success();
    }

    /// <summary>Échec du Saga (ex. stock indisponible) avant paiement.</summary>
    public void Fail(string reason)
    {
        Status = OrderStatus.Failed;
        CancellationReason = reason;
    }
}
