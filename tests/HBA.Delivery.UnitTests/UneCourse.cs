using HBA.Deliveries.Domain.Deliveries;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace HBA.Delivery.UnitTests;

/// <summary>Fabrique de courses pour les tests.</summary>
internal static class UneCourse
{
    /// <summary>Instant de référence des tests.</summary>
    public static readonly DateTime Maintenant = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    public static DeliveryStop Collecte() => Arret("Boutique Ganhi", "Rond-point Ganhi");

    public static DeliveryStop Remise() => Arret("Awa Sossou", "Pharmacie Jonquet");

    private static DeliveryStop Arret(string contact, string repere)
    {
        var position = Coordinates.Create(6.36, 2.42);
        position.IsSuccess.Should().BeTrue("la fabrique de test doit produire des coordonnées valides");

        // NUMÉRO À 10 CHIFFRES, PAS 8.
        var arret = DeliveryStop.Create(contact, "+2290197000001", "cotonou", null, repere, null, position.Value);
        arret.IsSuccess.Should().BeTrue("la fabrique de test doit produire un arrêt valide");
        return arret.Value;
    }

    public static DeliveryPackage Colis()
    {
        var colis = DeliveryPackage.Create("Colis de test", null, false, false);
        colis.IsSuccess.Should().BeTrue("la fabrique de test doit produire un colis valide");
        return colis.Value;
    }

    /// <summary>Une course express, à l'état <c>Pending</c>.</summary>
    public static Course Express(
        decimal? valeurDeclaree = null,
        bool paiementALaLivraison = false)
    {
        var creation = Course.Create(
            reference: "REF-" + Guid.NewGuid().ToString("n")[..8],
            source: DeliverySource.HbaExpress,
            type: DeliveryType.Express,
            pickup: Collecte(),
            dropoff: Remise(),
            package: Colis(),
            declaredValue: valeurDeclaree,
            isCashOnDelivery: paiementALaLivraison,
            partnerId: null,
            scheduledForUtc: null,
            nowUtc: Maintenant);

        creation.IsSuccess.Should().BeTrue("la fabrique de test doit produire une course valide");
        return creation.Value;
    }

    /// <summary>Une course déjà proposée à ce livreur : il n'a plus qu'à accepter.</summary>
    public static Course ProposeeA(DriverId livreur)
    {
        var course = Express();
        course.StartSearching(Maintenant).IsSuccess.Should().BeTrue();
        course.AssignTo(livreur).IsSuccess.Should().BeTrue();
        return course;
    }
}
