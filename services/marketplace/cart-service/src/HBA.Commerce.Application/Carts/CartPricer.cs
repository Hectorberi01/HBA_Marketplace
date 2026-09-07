using HBA.Commerce.Contracts;
using HBA.Ordering.Contracts;
using HBA.Pricing.Contracts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Application.Carts;

/// <summary>
/// Valorise un panier : pour chaque ligne, appelle Pricing pour obtenir le prix
/// effectif (avec la trace des financeurs), puis agrège les totaux.
/// </summary>
internal static class CartPricer
{
    public static async Task<CartSummary> PriceAsync(
        CartAggregate cart,
        IPricingModuleApi pricing,
        IOrderingModuleApi ordering,
        CancellationToken cancellationToken)
    {
        var lines = new List<CartLineSummary>(cart.Items.Count);
        decimal subtotal = 0m, totalSeller = 0m, totalPlatform = 0m, grand = 0m;

        // CES DEUX VALEURS ÉTAIENT CODÉES EN DUR — `Code: null` ET `IsFirstOrder:
        // false`.
        var isFirstOrder = !await ordering.HasPlacedOrderAsync(cart.BuyerId, cancellationToken);

        // LE SOUS-TOTAL DU PANIER EST CALCULÉ AVANT LA BOUCLE, ET IL EST
        // INDISPENSABLE À LA JUSTESSE DES REMISES (D28).
        var cartSubtotal = cart.Items.Sum(i => i.UnitBaseAmount * i.Quantity);

        foreach (var item in cart.Items)
        {
            // UNE LIGNE FOOD PORTE DES IDENTIFIANTS VIDES VERS PRICING.
            var request = new PriceRequest(
                BaseAmount: item.UnitBaseAmount,
                Currency: item.Currency,
                ProductId: item.ProductId,
                CategoryId: item.CategoryId,
                SellerId: item.SellerId,
                Quantity: item.Quantity,
                Subtotal: item.UnitBaseAmount * item.Quantity,
                Code: cart.PromotionCode,
                IsFirstOrder: isFirstOrder,

                // L'ACHETEUR VOYAGE : le plafond par compte se compte sur un
                // `UserId`, et une évaluation qui ne le porte pas le rend
                // indéterminable — donc inapplicable.
                BuyerId: cart.BuyerId,
                CartSubtotal: cartSubtotal);

            var b = await pricing.CalculatePriceAsync(request, cancellationToken);

            var lineSeller = b.SellerDiscount * item.Quantity;
            var linePlatform = b.PlatformDiscount * item.Quantity;
            var lineTotal = b.FinalAmount * item.Quantity;

            lines.Add(new CartLineSummary(
                item.Id, item.Kind.ToString(),
                item.OfferId, item.ProductId, item.CategoryId, item.SellerId, item.Sku,
                item.ShipFromLocationId, item.Quantity, item.UnitBaseAmount,
                b.SellerDiscount, b.PlatformDiscount, b.FinalAmount, lineTotal, item.Currency,
                item.RestaurantId, item.MenuItemId, item.Notes,
                item.Options.Select(o => new CartLineOptionSummary(o.OptionGroupId, o.OptionId)).ToList()));

            subtotal += item.UnitBaseAmount * item.Quantity;
            totalSeller += lineSeller;
            totalPlatform += linePlatform;
            grand += lineTotal;
        }

        return new CartSummary(
            cart.Id.Value, cart.BuyerId, cart.Currency, cart.Status.ToString(),
            cart.Kind?.ToString(),
            lines, subtotal, totalSeller, totalPlatform, grand, cart.PromotionCode);
    }
}
