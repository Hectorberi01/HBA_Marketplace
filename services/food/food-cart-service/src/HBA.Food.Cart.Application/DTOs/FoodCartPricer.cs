using HBA.FoodCarts.Contracts;
using HBA.Pricing.Contracts;
using CartAggregate = HBA.FoodCarts.Domain.Carts.FoodCart;

namespace HBA.FoodCarts.Application.Carts;

/// <summary>
/// Valorise un panier de repas : pour chaque ligne, demande à Pricing le prix
/// effectif, puis agrège. Le panier ne stocke jamais ces prix — ils sont
/// recalculés à chaque lecture.
/// </summary>
internal static class FoodCartPricer
{
    public static async Task<FoodCartSummary> PriceAsync(
        CartAggregate cart,
        IPricingModuleApi pricing,
        bool isFirstOrder,
        CancellationToken cancellationToken)
    {
        var lignes = new List<FoodCartLineSummary>(cart.Items.Count);
        decimal soustotal = 0m, totalVendeur = 0m, totalPlateforme = 0m, total = 0m;

        foreach (var ligne in cart.Items)
        {
            // ═════════════════════════════════════════════════════════════════
            // PRODUIT, CATÉGORIE ET VENDEUR SONT VIDES, ET C'EST UNE LIMITE
            //    CONNUE — PAS UN OUBLI.
            //
            // Une promotion CIBLÉE ne peut donc pas s'appliquer à un repas :
            // seules les promotions générales et les codes valent ici. Cibler un
            // restaurant ou une carte supposerait que Pricing sache ce qu'est un
            // restaurant, et ce vocabulaire n'existe pas de son côté.
            //
            // La séparation ne change rien à cela — elle le rend seulement
            // lisible. Dans l'ancien panier, ces trois zéros étaient des champs
            // « de l'autre nature » laissés vides ; ici ce sont des arguments
            // qu'on passe explicitement, avec la raison écrite au-dessus.
            //
            // Le jour où ce besoin apparaîtra, c'est `PriceRequest` qu'il faudra
            // étendre — pas ce fichier.
            // ═════════════════════════════════════════════════════════════════
            var demande = new PriceRequest(
                BaseAmount: ligne.UnitBaseAmount,
                Currency: ligne.Currency,
                ProductId: Guid.Empty,
                CategoryId: Guid.Empty,
                SellerId: Guid.Empty,
                Quantity: ligne.Quantity,
                Subtotal: ligne.UnitBaseAmount * ligne.Quantity,
                Code: cart.PromotionCode,
                IsFirstOrder: isFirstOrder);

            var detail = await pricing.CalculatePriceAsync(demande, cancellationToken);

            var remiseVendeur = detail.SellerDiscount * ligne.Quantity;
            var remisePlateforme = detail.PlatformDiscount * ligne.Quantity;
            var totalLigne = detail.FinalAmount * ligne.Quantity;

            lignes.Add(new FoodCartLineSummary(
                ligne.Id,
                ligne.MenuItemId,
                ligne.NameSnapshot,
                ligne.Quantity,
                ligne.UnitBaseAmount,
                detail.SellerDiscount,
                detail.PlatformDiscount,
                detail.FinalAmount,
                totalLigne,
                ligne.Currency,
                ligne.Notes,
                ligne.Options
                    .Select(o => new FoodCartLineOptionSummary(o.OptionGroupId, o.OptionId))
                    .ToList()));

            soustotal += ligne.UnitBaseAmount * ligne.Quantity;
            totalVendeur += remiseVendeur;
            totalPlateforme += remisePlateforme;
            total += totalLigne;
        }

        return new FoodCartSummary(
            cart.Id.Value,
            cart.BuyerId,
            cart.RestaurantId,
            cart.Currency,
            cart.Status.ToString(),
            lignes,
            soustotal,
            totalVendeur,
            totalPlateforme,
            total,
            cart.PromotionCode);
    }
}
