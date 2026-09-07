// DÉPLACÉ DEPUIS `driver-service/src/HBA.Delivery.Driver.Domain/ValueObjects` (lot
// 5.4, ISSUE-069).

namespace HBA.Deliveries.Domain.Drivers;

/// <summary>CE QUE CHAQUE VÉHICULE PEUT PORTER — ÉCRIT UNE SEULE FOIS.</summary>
public static class VehicleCapacity
{
    /// <summary>À pied : ce qu'on porte sans se blesser sur quelques centaines de mètres.</summary>
    public const decimal OnFootMaxKg = 5m;

    /// <summary>Vélo : un panier avant, pas davantage.</summary>
    public const decimal BicycleMaxKg = 10m;

    /// <summary>Moto : le seuil vient de l'usage.</summary>
    public const decimal MotorcycleMaxKg = 30m;

    /// <summary>Tricycle à moteur : la charge utile courante au Bénin.</summary>
    public const decimal TricycleMaxKg = 150m;

    /// <summary>Voiture : coffre et banquette arrière.</summary>
    public const decimal CarMaxKg = 200m;

    /// <summary>Du plus petit au plus grand.</summary>
    public static IReadOnlyList<VehicleType> ByIncreasingCapacity { get; } =
    [
        VehicleType.OnFoot,
        VehicleType.Bicycle,
        VehicleType.Motorcycle,
        VehicleType.Tricycle,
        VehicleType.Car,
        VehicleType.Van
    ];

    /// <summary>
    /// Charge maximale. <c> null</c> = pas de limite utile : une camionnette porte
    /// plus que ce que le type <c> Weight</c> accepte de déclarer (300 kg).
    /// </summary>
    public static decimal? MaxWeightKg(VehicleType vehicle) => vehicle switch
    {
        VehicleType.OnFoot => OnFootMaxKg,
        VehicleType.Bicycle => BicycleMaxKg,
        VehicleType.Motorcycle => MotorcycleMaxKg,
        VehicleType.Tricycle => TricycleMaxKg,
        VehicleType.Car => CarMaxKg,
        VehicleType.Van => null,

        // PAS DE « _ => null ». Un véhicule ajouté à l'énumération et oublié ici
        // doit CASSER, bruyamment, à la première utilisation. Le renvoyer comme «
        // sans limite » le laisserait accepter n'importe quelle charge ; le
        // renvoyer comme « zéro » le rendrait invisible au dispatch, en silence.
        // Les deux oublis ont déjà eu lieu dans ce module.
        _ => throw new ArgumentOutOfRangeException(
            nameof(vehicle), vehicle,
            "Capacité de charge non définie pour ce véhicule : ajoutez-la à VehicleCapacity.")
    };

    /// <summary>Vélo et marche exigent un poids DÉCLARÉ.</summary>
    public static bool RequiresDeclaredWeight(VehicleType vehicle)
        => vehicle is VehicleType.OnFoot or VehicleType.Bicycle;

    /// <summary>
    /// Ce véhicule peut-il porter ce colis ? <paramref name="weightKg"/> nul = non
    /// déclaré.
    /// </summary>
    public static bool CanCarry(VehicleType vehicle, decimal? weightKg)
    {
        if (weightKg is null)
        {
            return !RequiresDeclaredWeight(vehicle);
        }

        var max = MaxWeightKg(vehicle);
        return max is null || weightKg <= max;
    }

    /// <summary>Le plus petit véhicule capable de porter ce poids, parmi ceux proposés.</summary>
    public static VehicleType? SmallestCapableOf(decimal? weightKg)
    {
        foreach (var vehicle in ByIncreasingCapacity)
        {
            if (CanCarry(vehicle, weightKg))
            {
                return vehicle;
            }
        }

        return null;
    }
}
