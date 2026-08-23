using HBA.Shared.Domain.Results;

namespace HBA.Delivery.Driver.Domain.ValueObjects;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE PLAQUE D'IMMATRICULATION.
///
/// `Create` LEVAIT UNE `ArgumentException` SUR UNE PLAQUE VIDE.
///
/// C'est un REFUS MÉTIER — le livreur a mal saisi son formulaire — et le dépôt
/// s'est donné pour règle de ne jamais l'exprimer par une exception : elle
/// remonterait au middleware d'erreurs et sortirait en 500, c'est-à-dire en
/// « réessayez » à un client qui doit corriger sa saisie. Elle rend désormais un
/// <see cref="Result{T}"/>, comme partout ailleurs.
///
/// CE QUI N'EST PAS VÉRIFIÉ : LE FORMAT. Le Bénin utilise plusieurs formats de
/// plaques et les livreurs immatriculés au Nigéria ou au Togo roulent aussi. Une
/// expression régulière écrite ici refuserait des plaques valides, ce qui est pire
/// que d'en accepter une fausse — la plaque sert à identifier un véhicule lors
/// d'un litige, pas à décider d'un droit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
