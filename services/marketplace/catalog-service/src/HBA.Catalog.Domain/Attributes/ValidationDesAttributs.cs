using System.Globalization;
using System.Text.RegularExpressions;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Domain.Attributes;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA VALIDATION DES ATTRIBUTS D'UNE FICHE CONTRE LE SCHÉMA DE SA CATÉGORIE.
///
/// SANS ELLE, `attribute_definitions` NE SERAIT QU'UNE DÉCORATION.
///
/// Deux tables, un formulaire dynamique, et rien qui vérifie que ce que le vendeur
/// a saisi correspond à ce qui était demandé : un `screen_size` déclaré DECIMAL
/// accepterait « grand », un `color` à choix accepterait « bleu-vert » absent de la
/// liste, et la vitrine filtrerait sur des valeurs qu'aucun filtre ne propose.
///
/// ELLE VIT DANS LE DOMAINE, PAS DANS UN VALIDATEUR FLUENTVALIDATION.
///
/// FluentValidation regarde une commande ; cette règle a besoin du SCHÉMA de la
/// catégorie, qui vient de la base. C'est le handler qui charge le schéma, et le
/// domaine qui décide — sinon la règle se retrouverait dans un validateur qui, pour
/// travailler, devrait interroger un dépôt.
///
/// ELLE NE REFUSE PAS LES ATTRIBUTS INCONNUS, ET C'EST DÉLIBÉRÉ.
///
/// Les fiches saisies avant l'existence de ces définitions portent des clés qui ne
/// correspondent à rien. Les refuser rendrait chacune impossible à resoumettre —
/// donc à corriger — alors que rien dans leur contenu n'est faux. Ce qui est
/// contrôlé, c'est ce que la catégorie DEMANDE.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ValidationDesAttributs
{
    /// <summary>
    /// Séparateur des valeurs multiples.
    ///
    /// LA BARRE VERTICALE, PAS LA VIRGULE.
    ///
    /// Les options sont des libellés saisis par un administrateur : « Noir, mat »
    /// est une valeur plausible. Découper sur la virgule en ferait deux valeurs
    /// dont aucune n'existe dans la liste, et le vendeur verrait sa fiche refusée
    /// pour un choix qu'il a bien fait.
    /// </summary>
    public const char SeparateurMultiple = '|';

    private static readonly Regex CouleurHexadecimale =
        new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    public static Result Valider(
        IReadOnlyList<AttributDeCategorie> schema,
        IReadOnlyDictionary<string, string>? valeurs)
    {
        if (schema is null || schema.Count == 0)
        {
            // Une catégorie sans schéma n'impose rien. C'est le cas de la totalité
            // du catalogue tant que personne n'a défini d'attributs.
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
                //
                // La valeur voyage en JSON et est comparée en SQL. L'accepter en
                // culture locale ferait entrer « 6,3 » en base, où toute comparaison
                // numérique — un filtre « écran > 6 pouces » — cesserait de
                // fonctionner sans erreur.
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

            // TEXT et TEXTAREA : toute chaîne non vide convient. La longueur est
            // bornée par la colonne jsonb, pas par une règle métier — un libellé
            // trop long est un problème d'affichage, pas de validité.
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
