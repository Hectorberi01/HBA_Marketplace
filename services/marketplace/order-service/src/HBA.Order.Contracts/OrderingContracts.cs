namespace HBA.Orders.Contracts;

/// <summary>Une option retenue sur un plat commandé. Identifiants seuls : voir `OrderLineOption`.</summary>
public sealed record OrderLineOptionSummary(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Une ligne de commande.
///
/// DEUX NATURES, ET <paramref name="Kind"/> DIT LAQUELLE LIRE.
///
/// « Goods » renseigne l'offre, le SKU et le lieu d'expédition ; « Food »
/// renseigne le restaurant, le plat et ses options. Les champs de l'autre nature
/// sont vides.
/// </summary>
/// <param name="Kind">« Goods » ou « Food ». Décide de tout ce qui suit le paiement.</param>
public sealed record OrderLineSummary(
    string Kind,
    Guid OfferId,
    Guid ProductId,
    Guid SellerId,
    string Sku,
    Guid ShipFromLocationId,
    int Quantity,
    decimal UnitBasePrice,
    decimal SellerDiscount,
    decimal PlatformDiscount,
    decimal FinalUnitPrice,
    decimal LineTotal,

    // ── Restauration : vides pour une ligne de marchandise ──────────────────
    Guid RestaurantId = default,
    Guid MenuItemId = default,
    string? Notes = null,
    IReadOnlyList<OrderLineOptionSummary>? Options = null);

/// <summary>
/// Adresse de livraison figée sur la commande (instantané).
///
/// <c>CommuneName</c> est résolu à la lecture depuis <c>CommuneCode</c> : seul le code est
/// stocké. <c>Landmark</c> est le point de repère — au Bénin, c'est l'information que le
/// livreur utilise réellement, bien avant la rue.
/// </summary>
public sealed record OrderShippingAddressSummary(
    string? Label,
    string? Recipient,
    string? CommuneCode,
    string? CommuneName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    string? CountryCode,
    double? Latitude,
    double? Longitude,
    string? Phone);

/// <summary>Vue publique d'une commande.</summary>
/// <param name="Kind">La nature de la commande : « Goods » ou « Food ».</param>
/// <param name="RestaurantId">L'établissement qui prépare, pour un repas. Null sinon.</param>
public sealed record OrderSummary(
    Guid Id,
    Guid BuyerId,
    Guid CartId,
    string Currency,
    string Status,
    DateTime CreatedAtUtc,
    decimal Subtotal,
    decimal TotalSellerDiscount,
    decimal TotalPlatformDiscount,
    decimal GrandTotal,
    IReadOnlyList<OrderLineSummary> Lines,
    OrderShippingAddressSummary? ShippingAddress = null,
    decimal ShippingFee = 0m,

    string Kind = "Goods",
    Guid? RestaurantId = null,

    /// <summary>Le devis de course déjà payé. Restauration seulement.</summary>
    string? DeliveryQuoteId = null,

    /// <summary>
    /// Pourquoi la commande est en ARBITRAGE — « la course a été annulée », « 2
    /// lieux d'expédition ». Nul si elle ne l'a jamais été.
    ///
    /// SANS CE CHAMP, LA CONSOLE D'ARBITRAGE EST INUTILISABLE. Un statut
    /// « UnderReview » sans motif ne dit pas à l'exploitation s'il faut relancer
    /// une course ou rembourser — c'est-à-dire précisément la décision qu'on lui
    /// demande de prendre.
    /// </summary>
    string? ReviewReason = null,

    /// <summary>Depuis quand elle attend une décision. C'est le tri de la file.</summary>
    DateTime? UnderReviewSinceUtc = null,

    /// <summary>
    /// La part de CE vendeur dans cette commande, quand la vue est celle d'un
    /// vendeur. Nul partout ailleurs — et nul, aussi, sur les commandes
    /// CONFIRMÉES AVANT que l'agrégat n'existe (voir la migration
    /// `CommandeParVendeur`).
    ///
    /// RENSEIGNÉ PAR `OrderMapper.ToSellerSummary` SEULEMENT. La vue acheteur
    /// et la console d'administration montrent la commande ENTIÈRE : y poser la
    /// part d'un vendeur reviendrait à en désigner un arbitrairement sur une
    /// commande qui en compte deux.
    /// </summary>
    Guid? SellerOrderId = null,

    /// <summary>
    /// Où en est CE vendeur : « AwaitingConfirmation », « Confirmed »,
    /// « Preparing », « ReadyForPickup », « HandedOver », « Rejected »,
    /// « Cancelled ».
    ///
    /// À NE PAS CONFONDRE AVEC <c>Status</c>, QUI EST CELUI DE LA COMMANDE.
    ///
    /// Les deux coexistent parce qu'ils ne parlent pas de la même chose :
    /// `Status` dit où en est le PAIEMENT et la livraison de l'ensemble, celui-ci
    /// dit ce que ce vendeur-là a encore à faire. Une commande peut être
    /// « Confirmed » globalement pendant qu'un de ses deux vendeurs n'a rien
    /// accepté — c'est très exactement ce qui n'était pas exprimable avant
    /// ISSUE-027.
    ///
    /// Nul quand la commande n'a pas de part vendeur : repas, ou commande
    /// confirmée avant la migration.
    /// </summary>
    string? SellerOrderStatus = null);

public sealed record OrderReturnContext(
    Guid OrderId,
    Guid CustomerId,
    Guid SellerId,
    Guid StoreId,
    Guid? SellerOrderId,
    DateTime DeliveredAtUtc,
    string PaymentId,
    string Currency,
    decimal CapturedAmount,
    decimal AlreadyRefundedAmount,
    IReadOnlyList<OrderReturnLineContext> Lines);

public sealed record OrderReturnLineContext(
    Guid OrderItemId,
    Guid ProductId,
    Guid? VariantId,
    Guid CategoryId,
    string Sku,
    string Name,
    int OrderedQuantity,
    int DeliveredQuantity,
    int AlreadyReturnedQuantity,
    decimal UnitPaidAmount);
