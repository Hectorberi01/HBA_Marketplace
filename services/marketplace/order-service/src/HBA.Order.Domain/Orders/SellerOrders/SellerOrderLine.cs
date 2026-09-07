using HBA.Shared.Domain.Primitives;

namespace HBA.Orders.Domain.Orders.SellerOrders;

/// <summary>Une ligne de la commande, vue par le vendeur qui la vend.</summary>
public sealed class SellerOrderLine : Entity<Guid>
{
    private SellerOrderLine()
    {
    }

    internal SellerOrderLine(
        Guid id,
        Guid orderLineId,
        Guid productId,
        string sku,
        Guid shipFromLocationId,
        int quantity,
        decimal unitPaidAmount)
        : base(id)
    {
        OrderLineId = orderLineId;
        ProductId = productId;
        Sku = sku;
        ShipFromLocationId = shipFromLocationId;
        Quantity = quantity;
        UnitPaidAmount = unitPaidAmount;
    }

    /// <summary>La ligne d'origine dans <c>ordering.order_lines</c>.</summary>
    public Guid OrderLineId { get; private set; }

    public Guid ProductId { get; private set; }

    /// <summary>Non nul, possiblement vide — même convention qu'`OrderLine.Sku`.</summary>
    public string Sku { get; private set; } = default!;

    /// <summary>
    /// D'où part le colis. Sans lui, aucun stock ne se rend (voir l'événement de
    /// refus).
    /// </summary>
    public Guid ShipFromLocationId { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Prix unitaire FINAL payé, remises comprises.</summary>
    public decimal UnitPaidAmount { get; private set; }

    /// <summary>Total payé pour cette ligne.</summary>
    public decimal LineTotal => UnitPaidAmount * Quantity;
}
