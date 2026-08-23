using HBA.FoodOrders.Domain.Orders.Events;
using HBA.Shared.Domain.Geography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.FoodOrders.Domain.Orders;

/// <summary>
/// Commande de repas : les plats d'UN restaurant, leur prix figé, l'adresse de
/// livraison et le devis de course. Agrégat racine — il possède ses lignes.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI `MealOrder` ET NON `FoodOrder`.
///
/// `FoodOrder` existe déjà, dans restaurant-service : c'est le TICKET DE CUISINE
/// — accepté, en préparation, prêt, retiré, avec ses postes et ses minutes. Son
/// propre code le dit : « ce n'est pas le statut commercial ».
///
/// Les deux agrégats décrivent le même repas à deux titres différents, et sont
/// reliés par `OrderId`. Leur donner le même nom obligerait à lire le namespace
/// pour savoir duquel on parle, dans un dépôt où six services les manipulent
/// tous les deux.
///
/// CE QUI A DISPARU PAR RAPPORT À `Order`, ET POURQUOI.
///
/// Pas de `Kind` : il n'y a qu'un univers. Pas de réservation de stock : un plat
/// n'existe pas avant d'être commandé — c'est ce qui explique que la décision du
/// restaurant vienne APRÈS le paiement. Pas de répartition par vendeur : elle
/// était vide par construction pour un repas, au prix d'un filtre explicite pour
/// qu'elle ne produise pas une part attribuée au vendeur « 00000000-… ».
///
/// Ce qui a été AJOUTÉ : `CartId` porte un index unique — le passage en commande
/// n'était pas idempotent, et un double-clic créait deux commandes et deux
/// paiements.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MealOrder : AggregateRoot<MealOrderId>
{
    private readonly List<MealOrderLine> _lines = new();

    private MealOrder()
    {
    }

    private MealOrder(
        MealOrderId id, Guid buyerId, Guid restaurantId, Guid cartId, string currency, string? promotionCode)
        : base(id)
    {
        BuyerId = buyerId;
        RestaurantId = restaurantId;
        CartId = cartId;
        Currency = currency;
        PromotionCode = promotionCode;
        Status = MealOrderStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BuyerId { get; private set; }

    /// <summary>
    /// L'établissement qui prépare cette commande.
    ///
    /// COLONNE, ET NON PROPRIÉTÉ DÉRIVÉE DE LA PREMIÈRE LIGNE.
    ///
    /// `Order.RestaurantId` se calculait en lisant `_lines[0].RestaurantId` après
    /// avoir vérifié `Kind == Food`. C'est ce que l'adaptateur vers la cuisine
    /// lisait pour savoir où envoyer le ticket : une donnée d'acheminement qui
    /// dépendait de l'ordre d'une collection. Ici elle est posée à la création et
    /// n'a pas de setter.
    /// </summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>
    /// Le panier dont cette commande est née.
    ///
    /// UNIQUE EN BASE — voir <c>IMealOrderRepository.GetByCartAsync</c>.
    /// </summary>
    public Guid CartId { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>
    /// Code promo du panier, FIGÉ au moment de la commande.
    ///
    /// La commande doit s'en souvenir : le panier est clos juste après, et le
    /// coupon n'est réellement décompté qu'à la CONFIRMATION. Sans cet
    /// instantané, Pricing recevrait « commande confirmée » sans savoir quel
    /// coupon consommer — et le coupon resterait éternellement disponible.
    /// </summary>
    public string? PromotionCode { get; private set; }

    public MealOrderStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal TotalSellerDiscount { get; private set; }

    public decimal TotalPlatformDiscount { get; private set; }

    /// <summary>Frais de course, fixés par le devis relu au paiement.</summary>
    public decimal ShippingFee { get; private set; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE DEVIS DE COURSE QUI A FIXÉ CES FRAIS.
    ///
    /// SANS LUI, LE CLIENT ET LA PLATEFORME N'ACHÈTENT PAS LA MÊME CHOSE.
    ///
    /// Les frais sont chiffrés au paiement, à la distance réelle. La course, elle,
    /// n'est créée que lorsque le sac est prêt — vingt à quarante minutes plus
    /// tard. Redemander un devis à ce moment produit un SECOND prix, qui peut
    /// différer : grille modifiée entre-temps, zone redécoupée, version de
    /// tarification incrémentée. Le client aurait payé un montant et la plateforme
    /// en aurait acheté un autre.
    ///
    /// Figer l'identifiant règle les deux à la fois : la course est créée AU PRIX
    /// DÉJÀ PAYÉ, et le devis orphelin créé au paiement devient celui qui sert.
    ///
    /// OBLIGATOIRE ICI, ALORS QU'IL ÉTAIT FACULTATIF CHEZ SON ANCÊTRE.
    ///
    /// `Order.DeliveryQuoteId` était nullable, avec la mention « restauration
    /// seulement » : la marchandise passait sans devis, frais à zéro, et la
    /// plateforme réglait la course. Ce compromis n'a pas de raison d'être pour un
    /// repas, où la course EST achetée au prix réel — d'où le refus explicite dans
    /// le gestionnaire de paiement.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? DeliveryQuoteId { get; private set; }

    public decimal GrandTotal { get; private set; }

    /// <summary>Un mot du client pour la cuisine, valable pour toute la commande.</summary>
    public string? CustomerNote { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Pourquoi la commande a été mise en ARBITRAGE.
    ///
    /// DISTINCT DE <see cref="CancellationReason"/>, ET IL DOIT LE RESTER. Une
    /// commande en arbitrage n'est pas annulée : elle est payée, encaissée, et
    /// l'exploitation va décider si on la relance ou si on la retourne. Réutiliser
    /// le champ d'annulation afficherait un motif d'annulation sur une vente
    /// encore vivante — et, le jour où l'arbitrage conclut au remboursement,
    /// écraserait la cause d'origine par la décision.
    /// </summary>
    public string? ReviewReason { get; private set; }

    /// <summary>
    /// Depuis quand elle attend une décision humaine.
    ///
    /// C'EST LE TRI DE LA FILE D'ARBITRAGE. Sans cette date, la console ne peut
    /// ordonner que par date de commande — et un dossier bloqué depuis trois jours
    /// passerait derrière un bloqué depuis dix minutes.
    /// </summary>
    public DateTime? UnderReviewSinceUtc { get; private set; }

    // ── Adresse de livraison, FIGÉE ─────────────────────────────────────────
    //
    // Copie, pas de clé étrangère : si le client modifie ou supprime son adresse
    // ensuite, la commande doit continuer de dire où le repas a été porté.
    //
    // On fige le CODE de commune, pas son libellé. Les codes ne changent jamais
    // (contrat de BeninGeography), alors qu'un libellé peut être corrigé — et une
    // correction d'orthographe ne doit pas réécrire l'historique.

    public string? ShipToLabel { get; private set; }
    public string? ShipToRecipient { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCommuneCode { get; private set; }
    public string? ShipToQuartier { get; private set; }
    public string? ShipToLandmark { get; private set; }
    public string? ShipToLine1 { get; private set; }
    public string? ShipToCountryCode { get; private set; }

    /// <summary>
    /// Position figée.
    ///
    /// OBLIGATOIRE POUR UN REPAS, LÀ OÙ ELLE EST FACULTATIVE POUR UN COLIS.
    ///
    /// La commune et le point de repère suffisent à un colis : le livreur a la
    /// journée pour trouver, et il appelle au besoin. Un plat chaud, non — la
    /// course est calculée à la distance RÉELLE, et sans coordonnées aucun devis
    /// n'est possible. Le contrôle est dans le gestionnaire de paiement : sans
    /// lui, la commande est payée, le repas cuisiné, et la course refusée au
    /// moment où le sac est prêt.
    /// </summary>
    public double? ShipToLatitude { get; private set; }

    public double? ShipToLongitude { get; private set; }

    /// <summary>Libellé de la commune, résolu à l'affichage.</summary>
    public string ShipToCommuneName => BeninGeography.CommuneName(ShipToCommuneCode);

    public IReadOnlyCollection<MealOrderLine> Lines => _lines.AsReadOnly();

    public static Result<MealOrder> Create(
        Guid buyerId,
        Guid restaurantId,
        Guid cartId,
        string currency,
        IEnumerable<MealOrderLineDraft> drafts,
        string? promotionCode = null,
        string? customerNote = null)
    {
        if (buyerId == Guid.Empty)
        {
            return Error.Validation("food_ordering.buyer_required", "L'acheteur est obligatoire.");
        }

        if (restaurantId == Guid.Empty)
        {
            return Error.Validation("food_ordering.restaurant_required", "Le restaurant est obligatoire.");
        }

        var lignes = drafts?.ToList() ?? [];
        if (lignes.Count == 0)
        {
            return Error.Validation("food_ordering.no_lines", "Une commande doit comporter au moins une ligne.");
        }

        if (lignes.Any(d => d.Quantity <= 0))
        {
            return Error.Validation(
                "food_ordering.line_quantity_invalid", "Chaque ligne doit avoir une quantité positive.");
        }

        // SANS PLAT, LA COMMANDE EST INENVOYABLE EN CUISINE.
        //
        // Elle serait payée, puis l'adaptateur vers la cuisine n'aurait rien à
        // adresser. L'échec doit se produire avant le paiement.
        if (lignes.Any(d => d.MenuItemId == Guid.Empty))
        {
            return Error.Validation(
                "food_ordering.line_incomplete", "Une ligne de repas doit désigner un plat.");
        }

        var commande = new MealOrder(
            MealOrderId.New(),
            buyerId,
            restaurantId,
            cartId,
            currency.Trim().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(promotionCode) ? null : promotionCode.Trim().ToUpperInvariant());

        commande.CustomerNote = Tronquer(customerNote, 500);

        foreach (var ligne in lignes)
        {
            commande._lines.Add(new MealOrderLine(Guid.NewGuid(), ligne));
        }

        commande.RecalculerTotaux();
        return commande;
    }

    /// <summary>Fige l'adresse de livraison choisie (copie intégrale dans la commande).</summary>
    public void SetShippingAddress(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, string? countryCode,
        double? latitude, double? longitude)
    {
        ShipToLabel = Tronquer(label, 60);
        ShipToRecipient = Tronquer(recipient, 120);
        ShipToPhone = Tronquer(phone, 20);
        ShipToCommuneCode = Tronquer(communeCode, 40);
        ShipToQuartier = Tronquer(quartier, 120);
        ShipToLandmark = Tronquer(landmark, 200);
        ShipToLine1 = Tronquer(line1, 200);
        ShipToCountryCode = Tronquer(countryCode, 2) ?? BeninGeography.CountryCode;

        // Les deux ou aucune : une latitude seule placerait le point dans le golfe
        // de Guinée, à 400 km de Cotonou.
        ShipToLatitude = latitude is not null && longitude is not null ? latitude : null;
        ShipToLongitude = ShipToLatitude is null ? null : longitude;
    }

    /// <summary>Fixe les frais de course d'après le devis relu, et recalcule le total.</summary>
    public void SetShippingFee(decimal fee, string deliveryQuoteId)
    {
        ShippingFee = fee < 0m ? 0m : fee;
        DeliveryQuoteId = string.IsNullOrWhiteSpace(deliveryQuoteId) ? null : deliveryQuoteId;
        RecalculerTotaux();
    }

    private void RecalculerTotaux()
    {
        Subtotal = _lines.Sum(l => l.UnitBasePrice * l.Quantity);
        TotalSellerDiscount = _lines.Sum(l => l.SellerDiscount * l.Quantity);
        TotalPlatformDiscount = _lines.Sum(l => l.PlatformDiscount * l.Quantity);
        GrandTotal = _lines.Sum(l => l.LineTotal) + ShippingFee;
    }

    /// <summary>La commande est enregistrée et attend son paiement.</summary>
    public Result MarkAwaitingPayment()
    {
        if (Status != MealOrderStatus.Pending)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Transition invalide vers « en attente de paiement »."));
        }

        Status = MealOrderStatus.AwaitingPayment;
        Raise(new MealOrderPlacedDomainEvent(
            Id.Value, BuyerId, RestaurantId, CartId, GrandTotal, Currency));
        return Result.Success();
    }

    /// <summary>Paiement encaissé.</summary>
    public Result MarkPaid()
    {
        if (Status != MealOrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Aucun paiement attendu dans cet état."));
        }

        Status = MealOrderStatus.Paid;
        return Result.Success();
    }

    /// <summary>La commande est confirmée : le ticket peut partir en cuisine.</summary>
    public Result Confirm()
    {
        if (Status != MealOrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "La commande doit être payée avant confirmation."));
        }

        Status = MealOrderStatus.Confirmed;

        // L'ÉVÉNEMENT PORTE LES LIGNES, ET C'EST TOUT L'INTÉRÊT.
        //
        // `OrderConfirmed` partait sans elles : le pont vers la cuisine testait
        // `Kind == "Food"`, RAPPELAIT order-service par gRPC pour obtenir le
        // détail, puis refiltrait les lignes sur `Kind`. Trois pas et un
        // aller-retour réseau, dont aucun n'existait pour une bonne raison —
        // seulement parce que l'événement servait deux univers et ne pouvait donc
        // rien porter de spécifique à l'un.
        Raise(new MealOrderConfirmedDomainEvent(
            Id.Value, BuyerId, RestaurantId, GrandTotal, ShippingFee, Currency,
            PromotionCode, DeliveryQuoteId, CustomerNote,
            _lines
                .Select(l => new MealOrderConfirmedLine(
                    l.Id,
                    l.MenuItemId,
                    l.Name,
                    l.Quantity,
                    l.FinalUnitPrice,
                    l.Notes,
                    l.Options.Select(o => (o.OptionGroupId, o.OptionId)).ToList()))
                .ToList()));

        return Result.Success();
    }

    /// <summary>
    /// Le repas a été remis au client. Déclenche, en aval, la libération de
    /// l'escrow et le reversement au restaurateur.
    /// </summary>
    public Result MarkDelivered()
    {
        // « EN ARBITRAGE » EST ACCEPTÉ ICI, ET C'EST DÉLIBÉRÉ.
        //
        // Une remise CONSTATÉE prouve que le repas est chez le client : le fait
        // est acquis, il n'est pas déduit. Refuser la clôture parce qu'un dossier
        // d'arbitrage traîne gèlerait l'escrow d'une livraison réellement faite,
        // et le restaurateur ne serait pas réglé.
        //
        // Le cas n'est pas théorique : une course annulée met la commande en
        // arbitrage, l'exploitation la relance, une SECONDE course aboutit — et le
        // message d'annulation de la première peut arriver après.
        if (Status is not (MealOrderStatus.Confirmed or MealOrderStatus.UnderReview))
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.invalid_transition", "Seule une commande confirmée peut être marquée livrée."));
        }

        Status = MealOrderStatus.Delivered;
        Raise(new MealOrderDeliveredDomainEvent(Id.Value, BuyerId, RestaurantId));
        return Result.Success();
    }

    /// <summary>
    /// Annulation par le client ou par un échec de paiement, AVANT la confirmation.
    /// </summary>
    public Result Cancel(string reason)
    {
        // UNE COMMANDE CONFIRMÉE NE S'ANNULE PAS PAR ICI.
        //
        // Elle est payée, le ticket est en cuisine : le geste correspondant est
        // `RejectByRestaurant` (la cuisine refuse) ou `CancelAfterReview`
        // (l'exploitation retourne la vente). Laisser passer ferait de la route
        // acheteur un moyen d'annuler un repas déjà en préparation, sans que le
        // restaurateur ne soit dédommagé de ce qu'il a engagé.
        if (Status is MealOrderStatus.Confirmed
            or MealOrderStatus.UnderReview
            or MealOrderStatus.Delivered
            or MealOrderStatus.Cancelled
            or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_cancellable", "La commande n'est plus annulable."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RESTAURANT A REFUSÉ — LA COMMANDE TOMBE APRÈS AVOIR ÉTÉ CONFIRMÉE.
    ///
    /// `Cancel` NE SUFFIT PAS, ET SON REFUS EST JUSTE.
    ///
    /// La restauration inverse la chronologie de la marchandise : le client paie,
    /// la commande est confirmée, ET ALORS la cuisine accepte ou refuse. Un refus
    /// n'est pas un incident, c'est une issue prévue — plus de riz, four en panne,
    /// fermeture imprévue.
    ///
    /// Sans cette transition, un refus laisserait une commande confirmée pour un
    /// repas que personne ne préparera, et un client débité sans recours.
    ///
    /// ELLE OUVRE UN DROIT, ELLE NE REND PAS L'ARGENT. Le remboursement
    /// appartient à financial-service, qui consomme l'annulation.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result RejectByRestaurant(string reason)
    {
        // « DÉJÀ LIVRÉE » SE DISTINGUE DE « DÉJÀ TERMINALE ».
        //
        // L'appelant absorbe `already_terminal` comme un rejeu — l'outbox livre au
        // moins une fois — puis poursuit vers le remboursement, lui-même
        // idempotent. Sur une commande LIVRÉE, le paiement est encaissé : il
        // rembourserait un repas déjà mangé. Le cas arrive avec une lettre morte
        // rejouée à la main, ou un message resté en souffrance pendant que la
        // course aboutissait.
        if (Status == MealOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_delivered", "La commande a déjà été livrée."));
        }

        if (Status is MealOrderStatus.Cancelled or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_terminal", "La commande n'est plus refusable dans cet état."));
        }

        // AVANT LE PAIEMENT, IL N'Y A RIEN À REFUSER.
        //
        // Le ticket de cuisine n'existe qu'après la confirmation. Autoriser un
        // refus sur une commande en attente de paiement ouvrirait un chemin
        // vicieux : elle passerait « annulée » pendant que le prestataire encaisse
        // encore derrière, produisant une capture orpheline que le remboursement
        // considérerait comme « rien à rembourser ».
        if (Status is MealOrderStatus.Pending or MealOrderStatus.AwaitingPayment)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_yet_paid", "Une commande non payée ne se refuse pas, elle s'annule."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>
    /// La commande est devenue inexécutable — elle passe en arbitrage.
    ///
    /// ON N'ANNULE PAS, ET ON NE REMBOURSE SURTOUT PAS D'OFFICE. Une course
    /// annulée est le plus souvent RÉATTRIBUABLE. La commande sort du « en
    /// cours » et entre dans une file où un humain tranche : relancer
    /// (<see cref="ResumeAfterReview"/>) ou retourner
    /// (<see cref="CancelAfterReview"/>).
    /// </summary>
    public Result MarkUnderReview(string reason)
    {
        if (Status == MealOrderStatus.Delivered)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_delivered", "La commande a déjà été livrée."));
        }

        // CODE PROPRE AU REJEU, PARCE QUE L'APPELANT DOIT POUVOIR L'AVALER.
        //
        // L'outbox livre AU MOINS une fois : le même message d'annulation de
        // course reviendra. Confondre ce cas avec « la commande n'est pas
        // arbitrable » ferait sortir une alerte pour un dossier correctement
        // ouvert.
        if (Status == MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_under_review", "La commande est déjà en arbitrage."));
        }

        if (Status is MealOrderStatus.Cancelled or MealOrderStatus.Failed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.already_terminal", "La commande n'est plus arbitrable dans cet état."));
        }

        if (Status != MealOrderStatus.Confirmed)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_confirmed", "Une commande non confirmée ne s'arbitre pas, elle s'annule."));
        }

        Status = MealOrderStatus.UnderReview;
        ReviewReason = reason;
        UnderReviewSinceUtc = DateTime.UtcNow;
        Raise(new MealOrderUnderReviewDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché : la commande REPART.
    /// </summary>
    /// <remarks>
    /// ON NE RELÈVE PAS LA CONFIRMATION, ET C'EST CAPITAL. Elle ouvre le ticket
    /// de cuisine, décompte le coupon et comptabilise les gains. Les rejouer ferait
    /// préparer le repas une seconde fois et brûlerait un second coupon. Reprendre
    /// une commande n'est pas la confirmer à nouveau : c'est lever une suspension.
    /// </remarks>
    public Result ResumeAfterReview()
    {
        if (Status != MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = MealOrderStatus.Confirmed;

        // ON EFFACE LA DATE, PAS LE MOTIF.
        //
        // La date dit « ce dossier attend depuis… » : la garder ferait réapparaître
        // une commande relancée en tête de la file. Le motif, lui, est de
        // l'HISTOIRE — c'est ce qui permet de comprendre, une semaine plus tard,
        // pourquoi ce repas a mis trois heures à partir.
        UnderReviewSinceUtc = null;

        Raise(new MealOrderResumedAfterReviewDomainEvent(
            Id.Value, BuyerId, RestaurantId, ReviewReason ?? string.Empty));
        return Result.Success();
    }

    /// <summary>
    /// L'exploitation a tranché dans l'autre sens : la vente est retournée, et le
    /// client sera remboursé.
    /// </summary>
    /// <remarks>
    /// CETTE MÉTHODE NE REMBOURSE PAS. Elle publie l'annulation ;
    /// financial-service rembourse en la consommant. Ce service annonce un fait,
    /// il n'ordonne pas un virement.
    /// </remarks>
    public Result CancelAfterReview(string reason)
    {
        if (Status != MealOrderStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict(
                "food_ordering.not_under_review", "La commande n'est pas en arbitrage."));
        }

        Status = MealOrderStatus.Cancelled;
        CancellationReason = reason;
        UnderReviewSinceUtc = null;
        Raise(new MealOrderCancelledDomainEvent(Id.Value, BuyerId, RestaurantId, reason));
        return Result.Success();
    }

    /// <summary>Échec avant paiement (devis introuvable, restaurant fermé…).</summary>
    public void Fail(string reason)
    {
        Status = MealOrderStatus.Failed;
        CancellationReason = reason;
    }

    private static string? Tronquer(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var propre = value.Trim();
        return propre.Length > max ? propre[..max] : propre;
    }
}
