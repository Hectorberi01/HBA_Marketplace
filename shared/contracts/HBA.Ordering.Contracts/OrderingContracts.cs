namespace HBA.Ordering.Contracts;

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
    string? DeliveryQuoteId = null);

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
