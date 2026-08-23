using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders;

/// <summary>Une option de plat à figer dans la commande. Voir `OrderLineOption`.</summary>
public sealed record OrderLineOptionDraft(Guid OptionGroupId, Guid OptionId);

/// <summary>
/// Données d'une ligne à figer au moment de la commande (snapshot prix issu de
/// Pricing). Sert d'entrée à la création de la commande.
///
/// DEUX NATURES DANS UN SEUL BROUILLON. Les champs de la nature opposée
/// restent vides — c'est <paramref name="Kind"/> qui dit lesquels lire.
/// </summary>
public sealed record OrderLineDraft(
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

    // ── Restauration : vides pour une ligne de marchandise ──────────────────
    OrderLineKind Kind = OrderLineKind.Goods,
    Guid RestaurantId = default,
    Guid MenuItemId = default,
    string? Notes = null,
    IReadOnlyList<OrderLineOptionDraft>? Options = null);

/// <summary>
/// Ligne de commande : un snapshot figé du prix au moment de l'achat (prix de
/// base, réductions par financeur, prix final). Les prix ne bougent plus après
/// la commande — base d'un payout vendeur auditable. Entité enfant de Order.
/// </summary>
public sealed class OrderLine : Entity<Guid>
{
    private readonly List<OrderLineOption> _options = new();

    private OrderLine()
    {
    }

    internal OrderLine(Guid id, OrderLineDraft draft)
        : base(id)
    {
        Kind = draft.Kind;
        RestaurantId = draft.RestaurantId;
        MenuItemId = draft.MenuItemId;
        Notes = draft.Notes;

        foreach (var option in draft.Options ?? Array.Empty<OrderLineOptionDraft>())
        {
            _options.Add(new OrderLineOption(Guid.NewGuid(), option.OptionGroupId, option.OptionId));
        }

        OfferId = draft.OfferId;
        ProductId = draft.ProductId;
        SellerId = draft.SellerId;
        Sku = draft.Sku;
        ShipFromLocationId = draft.ShipFromLocationId;
        Quantity = draft.Quantity;
        UnitBasePrice = draft.UnitBasePrice;
        SellerDiscount = draft.SellerDiscount;
        PlatformDiscount = draft.PlatformDiscount;
        FinalUnitPrice = draft.FinalUnitPrice;
    }

    /// <summary>Marchandise ou restauration. Décide de tout ce qui suit le paiement.</summary>
    public OrderLineKind Kind { get; private set; }

    // ── Restauration ────────────────────────────────────────────────────────

    /// <summary>L'établissement qui préparera ce plat. Vide pour une marchandise.</summary>
    public Guid RestaurantId { get; private set; }

    /// <summary>Le plat dans la carte du restaurant. Vide pour une marchandise.</summary>
    public Guid MenuItemId { get; private set; }

    /// <summary>« Sans piment ». Destiné à la cuisine, figé avec la commande.</summary>
    public string? Notes { get; private set; }

    /// <summary>Les options DEMANDÉES. Libellés et suppléments appartiennent à Food.</summary>
    public IReadOnlyCollection<OrderLineOption> Options => _options.AsReadOnly();

    // ── Marchandise ─────────────────────────────────────────────────────────
    public Guid OfferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public string Sku { get; private set; } = default!;
    public Guid ShipFromLocationId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitBasePrice { get; private set; }
    public decimal SellerDiscount { get; private set; }
    public decimal PlatformDiscount { get; private set; }
    public decimal FinalUnitPrice { get; private set; }

    /// <summary>Total payé pour la ligne (prix final unitaire × quantité).</summary>
    public decimal LineTotal => FinalUnitPrice * Quantity;

    /// <summary>
    /// Cette ligne doit-elle passer par la réservation de stock ?
    ///
    /// LA QUESTION SE POSE ICI PLUTÔT QUE CHEZ L'APPELANT.
    ///
    /// La saga de checkout parcourt les lignes une à une. Écrire le test sur place
    /// — `if (line.Kind == Goods)` — marcherait, et se recopierait ensuite dans la
    /// compensation, dans la confirmation, dans le retour marchandise. Quatre
    /// endroits, dont trois qu'on oublie le jour où une troisième nature apparaît.
    /// </summary>
    public bool RequiresStockReservation => Kind == OrderLineKind.Goods;
}
