using HBA.Shared.Domain.Results;

namespace HBA.Delivery.Driver.Domain.ValueObjects;

/// <summary>UNE PLAQUE D'IMMATRICULATION.</summary>
public sealed record VehiclePlate(string Value)
{
    public static Result<VehiclePlate> Create(string? value)
    {
        var normalise = Normalize(value);

        return normalise is null
            ? Result.Failure<VehiclePlate>(
                Error.Validation("driver.plate_required", "La plaque d'immatriculation est requise pour ce véhicule."))
            : new VehiclePlate(normalise);
    }

    /// <summary>Forme canonique, ou nul si la saisie est vide.</summary>
    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
