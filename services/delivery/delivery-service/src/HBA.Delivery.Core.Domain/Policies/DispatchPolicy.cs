// CE FICHIER VIVAIT DANS `dispatch-service/src/HBA.Delivery.Dispatch.Domain`.

using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using DeliveryAggregate = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace HBA.Deliveries.Domain.Dispatch;

/// <summary>Un livreur candidat, avec sa position au moment de la recherche.</summary>
/// <param name="Driver">Le livreur.</param>
/// <param name="Position">Sa position courante, issue du cache.</param>
public readonly record struct DriverCandidate(Driver Driver, Coordinates Position);

/// <summary>Un candidat retenu, avec sa note et le détail qui l'explique.</summary>
public readonly record struct ScoredDriver(DriverCandidate Candidate, double Score, double DistanceKm)
{
    public DriverId DriverId => Candidate.Driver.Id;
}

/// <summary>À QUI PROPOSER LA COURSE.</summary>
public static class DispatchPolicy
{
    /// <summary>Rayon de recherche par défaut.</summary>
    public const double DefaultRadiusKm = 5.0;

    /// <summary>Rayon élargi, utilisé après un premier tour infructueux.</summary>
    public const double ExtendedRadiusKm = 12.0;


    /// <summary>
    /// Au-delà, l'expérience ne départage plus : cent courses ou mille, c'est
    /// pareil.
    /// </summary>
    private const int ExperiencePlateau = 100;

    /// <summary>Poids de l'expérience dans la note finale.</summary>
    private const double ExperienceWeight = 0.20;

    /// <summary>Classe les candidats, du meilleur au moins bon.</summary>
    /// <param name="delivery">La course à pourvoir.</param>
    /// <param name="candidates">Livreurs trouvés dans le rayon.</param>
    /// <param name="radiusKm">Rayon retenu pour ce tour.</param>
    public static IReadOnlyList<ScoredDriver> Rank(
        DeliveryAggregate delivery,
        IEnumerable<DriverCandidate> candidates,
        double radiusKm = DefaultRadiusKm)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(candidates);

        // La distance se mesure jusqu'au point de COLLECTE, pas de remise : ce qui
        // retarde la course, c'est le temps qu'un livreur met à venir chercher le
        // colis.
        var origin = delivery.Pickup.Position;

        var alreadyRefused = delivery.Assignments
            .Where(a => a.Outcome is AssignmentOutcome.Rejected)
            .Select(a => a.DriverId)
            .ToHashSet();

        var scored = new List<ScoredDriver>();

        foreach (var candidate in candidates)
        {
            if (!candidate.Driver.CanReceiveOffers)
            {
                continue;
            }

            // Ne jamais reproposer à quelqu'un qui a déjà dit non.
            if (alreadyRefused.Contains(candidate.Driver.Id))
            {
                continue;
            }

            if (!CanCarry(candidate.Driver, delivery.Package))
            {
                continue;
            }

            var distanceKm = origin.DistanceKmTo(candidate.Position);
            if (distanceKm > radiusKm)
            {
                continue;
            }

            // Proximité ramenée entre 0 et 1 : 1 sur place, 0 à la limite du rayon.
            var proximity = 1.0 - (distanceKm / radiusKm);
            var score = proximity * (1.0 - ExperienceWeight) + Experience(candidate.Driver) * ExperienceWeight;

            scored.Add(new ScoredDriver(candidate, score, distanceKm));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DriverId.Value) // départage stable : deux appels donnent le même ordre
            .ToList();
    }

    /// <summary>Le véhicule peut-il porter ce colis ? Critère physique, éliminatoire.</summary>
    public static bool CanCarry(Driver driver, DeliveryPackage package)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(package);

        // CETTE MÉTHODE FUT UN « switch » AVEC UN « _ => false » FINAL, ET CE REPLI
        // A COÛTÉ CHER.
        return VehicleCapacity.CanCarry(driver.Vehicle, package.WeightKg);
    }

    private static double Experience(Driver driver)
        => Math.Min(driver.CompletedDeliveries, ExperiencePlateau) / (double)ExperiencePlateau;
}
