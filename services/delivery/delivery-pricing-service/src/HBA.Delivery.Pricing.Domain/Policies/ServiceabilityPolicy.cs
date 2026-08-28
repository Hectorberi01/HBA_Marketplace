using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Policies;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA DISTANCE EST UNE LIGNE DROITE, ET LE NOM DE LA MÉTHODE LE DIT.
///
/// <see cref="HaversineMeters"/> ne calcule pas un trajet : elle calcule la corde
/// entre deux points sur une sphère. Ce qui sépare cette classe d'un moteur de
/// routage n'est pas un réglage, c'est une capacité entière — la connaissance des
/// rues, des sens uniques et du trafic.
///
/// <see cref="DistanceRoutiereEstimeeMetres"/> a été ajoutée pour que le facteur
/// de correction s'applique AU MÊME ENDROIT pour le prix et pour la desserte.
/// Auparavant, <c>GetServiceabilityAsync</c> et <c>CreateQuoteAsync</c>
/// appelaient chacun <c>HaversineMeters</c> directement : deux chemins vers le
/// même chiffre, que rien n'obligeait à rester d'accord. Un facteur posé sur un
/// seul des deux aurait produit une plateforme qui refuse une course puis la
/// facture, ou l'inverse.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ServiceabilityPolicy
{
    public const int MaximumDistanceMeters = 25_000;

    public static bool IsServiceable(int distanceMeters) => distanceMeters <= MaximumDistanceMeters;

    /// <summary>
    /// Distance à vol d'oiseau, corrigée par <paramref name="facteurCorrectionUrbaine"/>.
    /// C'est la SEULE porte d'entrée pour un appelant qui veut une distance de
    /// trajet : <see cref="HaversineMeters"/> reste publique parce qu'elle est
    /// testée seule, mais l'utiliser directement contourne le facteur.
    /// </summary>
    public static int DistanceRoutiereEstimeeMetres(
        GeoPoint pickup, GeoPoint dropoff, decimal facteurCorrectionUrbaine)
        => (int)Math.Round(HaversineMeters(pickup, dropoff) * facteurCorrectionUrbaine,
                           MidpointRounding.AwayFromZero);

    public static int HaversineMeters(GeoPoint pickup, GeoPoint dropoff)
    {
        const double earthRadius = 6_371_000;
        var dLat = DegreesToRadians(dropoff.Latitude - pickup.Latitude);
        var dLon = DegreesToRadians(dropoff.Longitude - pickup.Longitude);
        var lat1 = DegreesToRadians(pickup.Latitude);
        var lat2 = DegreesToRadians(dropoff.Latitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return (int)Math.Round(earthRadius * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h)));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
}
