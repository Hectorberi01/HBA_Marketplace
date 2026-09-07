using MediatR;
using HBA.Orders.Application.Orders.Queries;
using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;

namespace HBA.Orders.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Ordering.</summary>
internal sealed class OrderingModuleApi : IOrderingModuleApi
{
    private readonly ISender _sender;
    private readonly IOrderRepository _orders;
    private readonly ISellerOrderRepository _sellerOrders;

    public OrderingModuleApi(ISender sender, IOrderRepository orders, ISellerOrderRepository sellerOrders)
    {
        _sender = sender;
        _orders = orders;
        _sellerOrders = sellerOrders;
    }

    /// <summary>
    /// Lecture directe au repository, sans passer par MediatR : c'est un simple
    /// EXISTS sur index, appelé à CHAQUE valorisation de panier.
    /// </summary>
    public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
        => _orders.HasPurchasedAsync(buyerId, cancellationToken);

    public async Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>
    /// Ce que return-refund doit savoir pour ouvrir un dossier et plafonner un
    /// remboursement.
    /// </summary>
    public async Task<OrderReturnContext?> GetOrderReturnContextAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(new OrderId(orderId), cancellationToken);
        if (order is null || order.Status != OrderStatus.Delivered || order.PaymentId is null)
        {
            return null;
        }

        var lines = order.Lines.Select(line => new OrderReturnLineContext(
            OrderItemId: line.Id,
            ProductId: line.ProductId,
            VariantId: null,
            CategoryId: Guid.Empty,
            Sku: line.Sku,
            Name: string.IsNullOrWhiteSpace(line.Sku) ? line.ProductId.ToString() : line.Sku,
            OrderedQuantity: line.Quantity,
            DeliveredQuantity: line.Quantity,
            AlreadyReturnedQuantity: order.ReturnedQuantityFor(line.Id),
            UnitPaidAmount: line.FinalUnitPrice)).ToList();

        var firstSellerId = lines.Count == 0
            ? Guid.Empty
            : order.Lines.First().SellerId;

        // La part de CE vendeur-là, pour que les deux champs désignent le même.
        var sellerOrder = firstSellerId == Guid.Empty
            ? null
            : await _sellerOrders.FindAsync(order.Id.Value, firstSellerId, cancellationToken);

        return new OrderReturnContext(
            OrderId: order.Id.Value,
            CustomerId: order.BuyerId,
            SellerId: firstSellerId,
            StoreId: firstSellerId,
            SellerOrderId: sellerOrder?.Id.Value,
            DeliveredAtUtc: order.CreatedAtUtc,
            PaymentId: order.PaymentId.Value.ToString(),
            Currency: order.Currency,
            CapturedAmount: order.GrandTotal,
            AlreadyRefundedAmount: order.RefundedAmount,
            Lines: lines);
    }

    /// <summary>
    /// Somme des quantités vendues par ce vendeur sur les commandes encaissées
    /// (Confirmed / Delivered).
    /// </summary>
    public Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => _orders.SumSoldQuantityBySellerAsync(sellerId, cancellationToken);
}
