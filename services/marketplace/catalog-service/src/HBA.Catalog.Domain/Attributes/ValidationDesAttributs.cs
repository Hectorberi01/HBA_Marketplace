using System.Globalization;
using System.Text.RegularExpressions;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Attributes;

/// <summary>LA VALIDATION DES ATTRIBUTS D'UNE FICHE CONTRE LE SCHÉMA DE SA CATÉGORIE.</summary>
public static class ValidationDesAttributs
{
    /// <summary>Séparateur des valeurs multiples.</summary>
    public const char SeparateurMultiple = '|';

    private static readonly Regex CouleurHexadecimale =
        new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    public static Result Valider(
        IReadOnlyList<AttributDeCategorie> schema,
        IReadOnlyDictionary<string, string>? valeurs)
    {
        if (schema is null || schema.Count == 0)
        {
            // Une catégorie sans schéma n'impose rien.
            return Result.Success();
        }

        var saisies = valeurs ?? new Dictionary<string, string>();

        foreach (var attribut in schema.OrderBy(a => a.Rattachement.DisplayOrder))
        {
            var code = attribut.Definition.Code;
            saisies.TryGetValue(code, out var brute);
            var valeur = brute?.Trim();

            if (string.IsNullOrEmpty(valeur))
            {
                if (attribut.Rattachement.Required)
                {
                    return Result.Failure(Error.BusinessRule(
                        "catalog.attribute.required_missing",
                        $"L'attribut « {attribut.Definition.Name} » est obligatoire pour cette catégorie."));
                }

                continue;
            }

            var controle = ValiderUneValeur(attribut.Definition, valeur);
            if (controle.IsFailure)
            {
                return controle;
            }
        }

        return Result.Success();
    }

    private static Result ValiderUneValeur(AttributeDefinition definition, string valeur)
    {
        switch (definition.Type)
        {
            case AttributeValueType.Integer:
                return int.TryParse(valeur, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? Result.Success()
                    : Invalide(definition, "un nombre entier");

            case AttributeValueType.Decimal:
                // CULTURE INVARIANTE : le point, jamais la virgule.
                return decimal.TryParse(valeur, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                    ? Result.Success()
                    : Invalide(definition, "un nombre décimal (point comme séparateur)");

            case AttributeValueType.Boolean:
                return bool.TryParse(valeur, out _)
                    ? Result.Success()
                    : Invalide(definition, "« true » ou « false »");

            case AttributeValueType.Date:
                return DateTime.TryParse(valeur, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)
                    ? Result.Success()
                    : Invalide(definition, "une date au format ISO 8601");

            case AttributeValueType.Color:
                return CouleurHexadecimale.IsMatch(valeur)
                    ? Result.Success()
                    : Invalide(definition, "une couleur hexadécimale, « #1A2B3C »");

            case AttributeValueType.Select:
                return definition.Options.Any(o => string.Equals(o, valeur, StringComparison.OrdinalIgnoreCase))
                    ? Result.Success()
                    : HorsListe(definition, valeur);

            case AttributeValueType.MultiSelect:
                foreach (var part in valeur.Split(SeparateurMultiple, StringSplitOptions.RemoveEmptyEntries))
                {
                    var choix = part.Trim();
                    if (!definition.Options.Any(o => string.Equals(o, choix, StringComparison.OrdinalIgnoreCase)))
                    {
                        return HorsListe(definition, choix);
                    }
                }

                return Result.Success();

            // TEXT et TEXTAREA : toute chaîne non vide convient.
            default:
                return Result.Success();
        }
    }

    private static Result Invalide(AttributeDefinition definition, string attendu)
        => Result.Failure(Error.BusinessRule(
            "catalog.attribute.value_invalid",
            $"L'attribut « {definition.Name} » attend {attendu}."));

    private static Result HorsListe(AttributeDefinition definition, string valeur)
        => Result.Failure(Error.BusinessRule(
            "catalog.attribute.value_not_allowed",
            $"« {valeur} » ne fait pas partie des valeurs proposées pour « {definition.Name} » : "
            + string.Join(", ", definition.Options)));
}
