using HBA.Orders.Contracts;
using HBA.Orders.Domain.Orders;
using HBA.Orders.Domain.Orders.SellerOrders;
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Orders.Application.Orders;

internal static class OrderMapper
{
    public static OrderSummary ToSummary(OrderAggregate order) => new(
        order.Id.Value,
        order.BuyerId,
        order.CartId,
        order.Currency,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.Subtotal,
        order.TotalSellerDiscount,
        order.TotalPlatformDiscount,
        order.GrandTotal,
        order.Lines.Select(l => new OrderLineSummary(
            l.Kind.ToString(),
            l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
            l.UnitBasePrice, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice, l.LineTotal,
            l.RestaurantId, l.MenuItemId, l.Notes,
            l.Options.Select(o => new OrderLineOptionSummary(o.OptionGroupId, o.OptionId)).ToList())).ToList(),
        order.HasShippingAddress
            ? new OrderShippingAddressSummary(
                order.ShipToLabel, order.ShipToRecipient,
                order.ShipToCommuneCode, order.ShipToCommuneName,
                order.ShipToQuartier, order.ShipToLandmark, order.ShipToLine1,
                order.ShipToCountryCode,
                order.ShipToLatitude, order.ShipToLongitude,
                order.ShipToPhone)
            : null,
        order.ShippingFee,
        order.Kind.ToString(),
        order.RestaurantId,
        order.DeliveryQuoteId,

        // Le motif d'arbitrage et son ancienneté : sans eux, la file d'arbitrage
        // affiche un statut sans dire QUOI trancher.
        order.ReviewReason,
        order.UnderReviewSinceUtc);

    /// <summary>LA MÊME COMMANDE, VUE PAR UN SEUL VENDEUR.</summary>
    /// <param name="sellerOrder">La part de CE vendeur, quand elle existe.</param>
    public static OrderSummary ToSellerSummary(
        OrderAggregate order, Guid sellerId, SellerOrder? sellerOrder = null)
    {
        var lignes = order.Lines.Where(l => l.SellerId == sellerId).ToList();

        var sousTotal = lignes.Sum(l => l.UnitBasePrice * l.Quantity);
        var remiseVendeur = lignes.Sum(l => l.SellerDiscount * l.Quantity);
        var remisePlateforme = lignes.Sum(l => l.PlatformDiscount * l.Quantity);
        var total = lignes.Sum(l => l.LineTotal);

        return new OrderSummary(
            order.Id.Value,
            order.BuyerId,
            order.CartId,
            order.Currency,
            order.Status.ToString(),
            order.CreatedAtUtc,
            sousTotal,
            remiseVendeur,
            remisePlateforme,
            total,
            lignes.Select(l => new OrderLineSummary(
                l.Kind.ToString(),
                l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
                l.UnitBasePrice, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice, l.LineTotal,
                l.RestaurantId, l.MenuItemId, l.Notes,
                l.Options.Select(o => new OrderLineOptionSummary(o.OptionGroupId, o.OptionId)).ToList())).ToList(),
            order.HasShippingAddress
                ? new OrderShippingAddressSummary(
                    order.ShipToLabel, order.ShipToRecipient,
                    order.ShipToCommuneCode, order.ShipToCommuneName,
                    order.ShipToQuartier, order.ShipToLandmark, order.ShipToLine1,
                    order.ShipToCountryCode,

                    // Coordonnées GPS et téléphone : retirés.
                    null, null, null)
                : null,
            0m,
            order.Kind.ToString(),
            order.RestaurantId,
            order.DeliveryQuoteId,
            order.ReviewReason,
            order.UnderReviewSinceUtc,

            // C'EST CE QUI DIT AU VENDEUR CE QU'IL A À FAIRE.
            sellerOrder?.Id.Value,
            sellerOrder?.Status.ToString());
    }
}
