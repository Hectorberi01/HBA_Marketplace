using HBA.Inventory.Domain.Common;
using HBA.Inventory.Domain.Stock;

namespace HBA.Inventory.UnitTests;

/// <summary>
/// Fabrique d'articles de stock pour les tests.
///
/// ON PASSE PAR `InventoryItem.Create`, PAS PAR UN CONSTRUCTEUR.
///
/// Les réservations ne sont accessibles que par les méthodes de l'agrégat
/// (`StockReservation` n'a qu'un constructeur `internal`). C'est voulu : un test
/// qui fabriquerait une réservation à la main éprouverait un état que le code de
/// production ne peut pas produire.
/// </summary>
internal static class UnArticleDeStock
{
    /// <summary>Instant de référence des tests. Fixe : une expiration se raisonne à la seconde.</summary>
    public static readonly DateTime Maintenant = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    public static InventoryItem Avec(int onHand, int seuil = 0)
    {
        var sku = Sku.Create("SKU-TEST-1").Value;
        var creation = InventoryItem.Create(sku, Guid.NewGuid(), onHand, seuil);
        creation.IsSuccess.Should().BeTrue("la fabrique de test doit produire un article valide");
        return creation.Value;
    }

    /// <summary>Échéance dans le futur : la réservation reste en cours.</summary>
    public static DateTime DansUnQuartDHeure => Maintenant.AddMinutes(15);

    /// <summary>Échéance déjà dépassée : le balayage doit la reprendre.</summary>
    public static DateTime IlYAUneHeure => Maintenant.AddHours(-1);

    public static StockReservation Reservation(this InventoryItem item, Guid orderId)
        => item.Reservations.Single(r => r.OrderId == orderId);
}
