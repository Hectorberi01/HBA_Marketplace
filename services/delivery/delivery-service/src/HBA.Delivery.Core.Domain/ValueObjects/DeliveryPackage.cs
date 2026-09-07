using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>CE QUI EST TRANSPORTÉ — DÉCRIT, JAMAIS INTERPRÉTÉ.</summary>
public sealed class DeliveryPackage : ValueObject
{
    private const int MaxDescription = 300;

    /// <summary>Au-delà, une moto n'est pas un bon choix par défaut.</summary>
    public const decimal MotorcycleMaxWeightKg = 30m;

    private DeliveryPackage(string description, decimal? weightKg, bool isFragile, bool isPerishable)
    {
        Description = description;
        WeightKg = weightKg;
        IsFragile = isFragile;
        IsPerishable = isPerishable;
    }

    // Requis par EF Core.
    private DeliveryPackage()
    {
        Description = string.Empty;
    }

    /// <summary>Ce que le livreur lit. Texte libre, jamais interprété par le code.</summary>
    public string Description { get; private init; }

    /// <summary>Poids déclaré. Facultatif : personne ne pèse un repas.</summary>
    public decimal? WeightKg { get; private init; }

    public bool IsFragile { get; private init; }

    /// <summary>Périssable : impose un délai court, pas un type de véhicule.</summary>
    public bool IsPerishable { get; private init; }

    /// <summary>
    /// Le colis peut-il partir à moto ? Un poids inconnu est présumé transportable.
    /// </summary>
    public bool FitsOnMotorcycle => WeightKg is null || WeightKg <= MotorcycleMaxWeightKg;

    public static Result<DeliveryPackage> Create(
        string? description,
        decimal? weightKg = null,
        bool isFragile = false,
        bool isPerishable = false)
    {
        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmed is null)
        {
            return Result.Failure<DeliveryPackage>(
                Error.Validation("delivery.package.description_required", "Une description du colis est requise."));
        }

        if (weightKg is < 0)
        {
            return Result.Failure<DeliveryPackage>(
                Error.Validation("delivery.package.weight_negative", "Le poids ne peut pas être négatif."));
        }

        // Un poids nul déclaré n'est pas une erreur de saisie : c'est un champ
        // laissé à zéro par une intégration.
        var normalizedWeight = weightKg is null or 0m ? null : weightKg;

        return new DeliveryPackage(
            trimmed.Length <= MaxDescription ? trimmed : trimmed[..MaxDescription],
            normalizedWeight,
            isFragile,
            isPerishable);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Description;
        yield return WeightKg;
        yield return IsFragile;
        yield return IsPerishable;
    }
}
