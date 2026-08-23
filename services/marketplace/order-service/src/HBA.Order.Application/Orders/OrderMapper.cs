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

        // Le motif d'arbitrage et son ancienneté : sans eux, la file
        // d'arbitrage affiche un statut sans dire QUOI trancher.
        order.ReviewReason,
        order.UnderReviewSinceUtc);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA MÊME COMMANDE, VUE PAR UN SEUL VENDEUR.
    ///
    /// LE CARNET DE COMMANDES VENDEUR RENDAIT LA COMMANDE ENTIÈRE.
    ///
    /// La route est pourtant bien gardée : appartenance vérifiée, capacité
    /// `ORDER_VIEW` exigée. Le trou n'était pas dans l'autorisation mais dans la
    /// PROJECTION — `ToSummary` mappe tout. Un vendeur d'une commande
    /// multi-vendeurs voyait donc :
    ///
    ///   • les lignes de ses CONCURRENTS — leurs produits, leurs prix, leurs
    ///     remises, leurs volumes. C'est du renseignement commercial servi par
    ///     l'API ;
    ///   • les totaux de la commande entière, qui ne sont pas les siens ;
    ///   • la LATITUDE, la LONGITUDE et le TÉLÉPHONE de l'acheteur.
    ///
    /// LES TOTAUX SONT RECALCULÉS, PAS SEULEMENT MASQUÉS.
    ///
    /// Laisser `GrandTotal` à sa valeur d'origine tout en filtrant les lignes
    /// serait pire que la fuite : le vendeur lirait un total qui ne correspond à
    /// rien de ce qu'il voit, et le prendrait pour ce qu'on lui doit.
    ///
    /// LE FRAIS DE PORT EST TOUJOURS MIS À ZÉRO, ET `SellerOrder` N'Y CHANGE RIEN.
    ///
    /// La note d'origine disait : « le répartir demanderait l'agrégat
    /// `SellerOrder`, qui n'existe pas ». Il existe désormais (ISSUE-027) — et le
    /// frais reste à zéro, parce que l'agrégat donnait l'OBJET qui pourrait le
    /// porter, pas la RÈGLE qui le répartit. Au prorata du montant ? du poids ?
    /// du nombre de colis ? Une commande à deux vendeurs achète UNE course, et
    /// aucune de ces trois clés n'est plus juste que les autres. Inventer la
    /// répartition ici mettrait un chiffre faux dans ce que le vendeur croit
    /// avoir vendu ; zéro reste au moins honnête, et la question appartient au
    /// règlement des vendeurs, pas à une projection de lecture.
    ///
    /// L'ADRESSE RESTE, LES COORDONNÉES PRÉCISES PARTENT.
    ///
    /// Le vendeur prépare un colis : il lui faut la commune, le quartier, le
    /// repère, le destinataire. Il ne livre pas — c'est delivery-service qui
    /// dépêche un livreur, et c'est LUI qui a besoin du GPS et du téléphone. Les
    /// servir au vendeur, c'est donner la position du domicile d'un acheteur à
    /// toute personne capable d'ouvrir un compte vendeur.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="sellerOrder">
    /// La part de CE vendeur, quand elle existe.
    ///
    /// NULLE POUR UNE COMMANDE CONFIRMÉE AVANT L'ARRIVÉE DE L'AGRÉGAT, et pour
    /// toute commande de repas. Le carnet doit rester lisible dans les deux cas :
    /// un vendeur dont l'historique n'a pas de parts verra ses anciennes commandes
    /// sans état vendeur, exactement comme avant — voir la migration
    /// `CommandeParVendeur` pour ce que la table naissant vide implique.
    /// </param>
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

                    // Coordonnées GPS et téléphone : retirés. Voir ci-dessus.
                    null, null, null)
                : null,
            0m,
            order.Kind.ToString(),
            order.RestaurantId,
            order.DeliveryQuoteId,
            order.ReviewReason,
            order.UnderReviewSinceUtc,

            // C'EST CE QUI DIT AU VENDEUR CE QU'IL A À FAIRE.
            //
            // `Status`, juste au-dessus, est celui de la COMMANDE : « Confirmed »
            // y veut dire « le paiement est encaissé ». Sans ces deux champs, le
            // carnet du vendeur affiche une commande payée et aucun geste à
            // poser — c'est-à-dire l'écran qu'ISSUE-026 décrit, où le parcours
            // s'arrête à la réception.
            sellerOrder?.Id.Value,
            sellerOrder?.Status.ToString());
    }
}
