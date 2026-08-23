using HBA.Orders.Domain.Orders;
using OrderAggregate = HBA.Orders.Domain.Orders.Order;

namespace HBA.Order.UnitTests;

/// <summary>
/// Fabrique de commandes pour les tests.
///
/// ON PASSE PAR `Order.Create` PUIS PAR LA SAGA, PAS PAR UN CONSTRUCTEUR.
///
/// `SellerOrder.SplitFrom` exige une commande CONFIRMÉE, et « confirmée » n'est
/// atteignable que par `MarkAwaitingPayment` → `MarkPaid` → `Confirm`. Un test
/// qui fabriquerait l'état à la main éprouverait une commande que le code de
/// production ne peut pas produire — et manquerait précisément l'invariant de
/// naissance des parts vendeur.
/// </summary>
internal static class UneCommande
{
    /// <summary>Instant de référence. Fixe : les horodatages de transition se comparent.</summary>
    public static readonly DateTime Maintenant = new(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Une ligne de marchandise vendue par <paramref name="sellerId"/>.</summary>
    public static OrderLineDraft Marchandise(
        Guid sellerId, int quantite = 1, decimal prixUnitaire = 1000m, string sku = "SKU-1")
        => new(
            OfferId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            SellerId: sellerId,
            Sku: sku,
            ShipFromLocationId: Guid.NewGuid(),
            Quantity: quantite,
            UnitBasePrice: prixUnitaire,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalUnitPrice: prixUnitaire);

    /// <summary>
    /// Une ligne de repas. Son <c>SellerId</c> est VIDE — c'est tout l'objet du
    /// filtre que le découpage doit respecter.
    /// </summary>
    public static OrderLineDraft Repas(Guid restaurantId, int quantite = 1, decimal prixUnitaire = 2500m)
        => new(
            OfferId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            SellerId: Guid.Empty,
            Sku: string.Empty,
            ShipFromLocationId: Guid.Empty,
            Quantity: quantite,
            UnitBasePrice: prixUnitaire,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            FinalUnitPrice: prixUnitaire,
            Kind: OrderLineKind.Food,
            RestaurantId: restaurantId,
            MenuItemId: Guid.NewGuid());

    /// <summary>Une commande CONFIRMÉE portant ces lignes.</summary>
    public static OrderAggregate Confirmee(params OrderLineDraft[] lignes)
    {
        var commande = Creee(lignes);
        Confirmer(commande);
        return commande;
    }

    /// <summary>Une commande à l'état initial (`Pending`).</summary>
    public static OrderAggregate Creee(params OrderLineDraft[] lignes)
    {
        var creation = OrderAggregate.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", lignes);
        creation.IsSuccess.Should().BeTrue("la fabrique de test doit produire une commande valide");
        return creation.Value;
    }

    /// <summary>Déroule la saga jusqu'à « confirmée ».</summary>
    public static void Confirmer(OrderAggregate commande)
    {
        commande.MarkAwaitingPayment().IsSuccess.Should().BeTrue();
        commande.MarkPaid(Guid.NewGuid()).IsSuccess.Should().BeTrue();
        commande.Confirm().IsSuccess.Should().BeTrue();
    }
}
