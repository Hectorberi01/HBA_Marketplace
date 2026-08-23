using HBA.Deliveries.Domain.Deliveries;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace HBA.Delivery.UnitTests;

/// <summary>
/// Fabrique de courses pour les tests.
///
/// ON PASSE PAR `Delivery.Create`, JAMAIS PAR UN CONSTRUCTEUR.
///
/// Les constructeurs de l'agrégat sont privés et les transitions sont les seules
/// portes d'entrée. Un test qui fabriquerait un état à la main éprouverait un
/// état que le code de production ne peut pas produire — c'est-à-dire rien.
///
/// L'ALIAS `Course` N'EST PAS UN CONFORT DE LECTURE, IL EST OBLIGATOIRE.
///
/// L'espace de noms de ce projet est `HBA.Delivery.UnitTests`. Écrire
/// `Delivery.Create(...)` ici ferait résoudre `Delivery` vers l'espace de noms
/// `HBA.Delivery` — pas vers le type. C'est la même collision que
/// `DeliveryConfiguration` contourne avec `Domain.Deliveries.Delivery`.
/// </summary>
internal static class UneCourse
{
    /// <summary>
    /// Instant de référence des tests. FIXE : une expiration de code se raisonne
    /// à la seconde, et `DateTime.UtcNow` rendrait les tests dépendants de
    /// l'heure à laquelle on les lance.
    /// </summary>
    public static readonly DateTime Maintenant = new(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

    public static DeliveryStop Collecte() => Arret("Boutique Ganhi", "Rond-point Ganhi");

    public static DeliveryStop Remise() => Arret("Awa Sossou", "Pharmacie Jonquet");

    private static DeliveryStop Arret(string contact, string repere)
    {
        var position = Coordinates.Create(6.36, 2.42);
        position.IsSuccess.Should().BeTrue("la fabrique de test doit produire des coordonnées valides");

        // NUMÉRO À 10 CHIFFRES, PAS 8.
        //
        // Cette fabrique portait « +22997000001 » — un numéro d'AVANT la migration
        // béninoise de 2024. `BeninGeography.NormalizePhone` exige désormais
        // exactement `LocalPhoneLength` (10) chiffres après l'indicatif et refuse
        // l'ancien format, délibérément : un numéro à 8 chiffres n'aboutit plus, et
        // l'accepter en silence revient à livrer un colis que personne ne pourra
        // annoncer. `DeliveryStop.Create` rendait donc un échec, et les QUINZE tests
        // qui passent par cette fabrique tombaient sur l'assertion ci-dessous — pas
        // sur ce qu'ils prétendaient éprouver.
        //
        // La forme retenue est celle qu'utilisent déjà `DossierLivreurTests` et
        // `SessionLivreurTests` (« +2290197000042 ») : ancien numéro préfixé de « 01 ».
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
