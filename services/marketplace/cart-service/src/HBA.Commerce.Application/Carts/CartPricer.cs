using HBA.Commerce.Contracts;
using HBA.Ordering.Contracts;
using HBA.Pricing.Contracts;
using CartAggregate = HBA.Commerce.Domain.Carts.Cart;

namespace HBA.Commerce.Application.Carts;

/// <summary>
/// Valorise un panier : pour chaque ligne, appelle Pricing pour obtenir le prix
/// effectif (avec la trace des financeurs), puis agrège les totaux. Le panier ne
/// stocke jamais ces prix — ils sont recalculés à chaque lecture.
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

        // ═════════════════════════════════════════════════════════════════════════
        // CES DEUX VALEURS ÉTAIENT CODÉES EN DUR — `Code: null` ET `IsFirstOrder: false`.
        //
        // Conséquence, et elle était totale : `Promotion.AppliesTo()` rejette toute promo
        // qui exige un code lorsque le code fourni ne correspond pas — et `null` ne
        // correspond jamais. `PromotionConditions.AreMet()` rejette toute promo
        // « première commande » lorsque `isFirstOrder` est faux — et il l'était toujours.
        //
        // Autrement dit, DEUX familles entières de promotions étaient inapplicables : le
        // back-office pouvait les créer, l'admin les activer, et aucun acheteur au monde
        // n'en aurait jamais vu la couleur. Le moteur tournait, mais à vide.
        //
        // Le coût de l'appel à Ordering est un EXISTS sur un index (BuyerId) — et il ne
        // sert qu'à répondre « cet acheteur a-t-il déjà commandé ? ». On l'évalue UNE fois
        // pour le panier, pas une fois par ligne : c'est une propriété de l'acheteur, pas
        // de l'article.
        // ═════════════════════════════════════════════════════════════════════════
        var isFirstOrder = !await ordering.HasPlacedOrderAsync(cart.BuyerId, cancellationToken);

        // ═════════════════════════════════════════════════════════════════════════
        // LE SOUS-TOTAL DU PANIER EST CALCULÉ AVANT LA BOUCLE, ET IL EST
        // INDISPENSABLE À LA JUSTESSE DES REMISES (D28).
        //
        // `CalculatePriceAsync` est appelée PAR LIGNE, mais un coupon est PAR
        // PANIER. Sans cette valeur, le fournisseur évaluerait « 1 000 F sur la
        // commande » contre chaque ligne séparément et accorderait mille francs
        // AUTANT DE FOIS qu'il y a de lignes.
        //
        // Elle est calculée sur les mêmes `UnitBaseAmount × Quantity` que la boucle
        // ci-dessous additionne dans `subtotal` : les deux ne peuvent pas diverger,
        // et c'est ce qui garantit que la somme des quote-parts imputées aux lignes
        // reste inférieure ou égale à la remise réellement accordée.
        // ═════════════════════════════════════════════════════════════════════════
        var cartSubtotal = cart.Items.Sum(i => i.UnitBaseAmount * i.Quantity);

        foreach (var item in cart.Items)
        {
            // UNE LIGNE FOOD PORTE DES IDENTIFIANTS VIDES VERS PRICING.
            //
            // Produit, catégorie et vendeur n'existent pas pour un plat. Une
            // promotion CIBLÉE ne peut donc pas s'y appliquer — seules les
            // promotions générales et les codes valent pour la restauration.
            //
            // C'est une limite connue, pas un oubli : cibler une promotion sur un
            // restaurant ou une carte suppose que Pricing sache ce qu'est un
            // restaurant, et ce vocabulaire n'existe pas de son côté. Le jour où
            // ce besoin apparaîtra, c'est `PriceRequest` qu'il faudra étendre.
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
