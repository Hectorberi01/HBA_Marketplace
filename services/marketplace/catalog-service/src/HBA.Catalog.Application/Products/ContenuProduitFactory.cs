using HBA.Catalog.Domain.Products;
using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Products;

/// <summary>Tarification telle qu'elle arrive du formulaire (§13, étape 4).</summary>
public sealed record TarificationSaisie(
    long BasePrice,
    long? CompareAtPrice = null,
    long? CostPrice = null,
    string? Currency = null,
    bool TaxIncluded = true,
    int TaxRate = 0);

/// <summary>Un défaut déclaré, en chaînes — c'est ce que le client envoie.</summary>
public sealed record DefautSaisi(
    string Type,
    string Location,
    string Description,
    string Severity);

/// <summary>Une ligne de fiche technique, en chaînes (§12).</summary>
public sealed record LigneSpecSaisie(string Name, string Value);

/// <summary>
/// Un groupe de la fiche technique (§12 : « Écran », « Processeur »…).
///
/// MIROIR DES TYPES DU DOMAINE, ET NON LES TYPES EUX-MÊMES.
///
/// `GroupeDeSpecifications` vit dans le domaine. L'exposer directement au contrat
/// HTTP ferait dépendre la forme du JSON public de la forme du domaine : le
/// renommer deviendrait un changement cassant pour les clients mobiles. Même
/// raison que pour `TarificationSaisie` et `ConditionSaisie`.
/// </summary>
public sealed record GroupeSpecSaisi(
    string Name,
    IReadOnlyList<LigneSpecSaisie> Items,
    int DisplayOrder = 0);

/// <summary>Condition commerciale telle qu'elle arrive du formulaire (§13, étape 3).</summary>
public sealed record ConditionSaisie(
    string Type = "New",
    string? Grade = null,
    string? Description = null,
    bool HasOriginalPackaging = false,
    bool HasOriginalAccessories = false,
    string FunctionalStatus = "FullyFunctional",
    IReadOnlyList<DefautSaisi>? Defects = null,
    string? RefurbishedByType = null,
    Guid? RefurbishedBySellerId = null,
    IReadOnlyList<string>? RefurbishmentOperations = null,
    int? BatteryHealthPercentage = null,
    bool? BatteryReplaced = null);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// DES CHAÎNES DU CLIENT AUX TYPES DU DOMAINE.
///
/// POURQUOI CETTE TRADUCTION NE VIT NI DANS LE DOMAINE, NI DANS L'ENDPOINT.
///
/// Pas dans le domaine : `ProductConditionType` ne doit rien savoir de « VERY_GOOD »
/// ni de la casse que choisit un client mobile. Un domaine qui parse des chaînes
/// de transport finit par porter les compromis de trois générations d'API.
///
/// Pas dans l'endpoint : la création et la mise à jour ont besoin exactement de la
/// même traduction. La dupliquer, c'est garantir qu'un jour l'une acceptera
/// « OPEN_BOX » et l'autre non — et que le vendeur ne comprendra pas pourquoi sa
/// modification est refusée alors que sa création passait.
///
/// ELLE REFUSE CE QU'ELLE NE CONNAÎT PAS, ELLE NE RETOMBE PAS SUR UN DÉFAUT.
///
/// La tentation est d'écrire « type inconnu → New ». Ce serait transformer une
/// faute de frappe du client en promesse commerciale : un vendeur qui envoie
/// « REFURBISHD » verrait sa fiche publiée en NEUF.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ContenuProduitFactory
{
    public static Result<ContenuProduit> Construire(
        string name,
        string description,
        Guid categoryId,
        TarificationSaisie tarification,
        ConditionSaisie? condition = null,
        string? shortDescription = null,
        string? productType = null,
        Guid? brandId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<string>? tags = null,
        Slug? slug = null,
        IReadOnlyList<GroupeSpecSaisi>? specifications = null)
    {
        if (tarification is null)
        {
            return Error.Validation("catalog.pricing.required", "La tarification de référence est obligatoire.");
        }

        var type = Analyser<ProductType>(productType, ProductType.Physical, "catalog.product.type_invalid",
            "Type de produit inconnu.");
        if (type.IsFailure)
        {
            return Result.Failure<ContenuProduit>(type.Error);
        }

        var pricing = ProductPricing.Create(
            tarification.BasePrice,
            tarification.CompareAtPrice,
            tarification.CostPrice,
            tarification.Currency,
            tarification.TaxIncluded,
            tarification.TaxRate);

        if (pricing.IsFailure)
        {
            return Result.Failure<ContenuProduit>(pricing.Error);
        }

        var conditionResult = ConstruireCondition(condition);
        if (conditionResult.IsFailure)
        {
            return Result.Failure<ContenuProduit>(conditionResult.Error);
        }

        return new ContenuProduit(
            name,
            description,
            categoryId,
            pricing.Value,
            conditionResult.Value,
            shortDescription,
            type.Value,
            brandId,
            attributes,
            tags,
            slug,
            (specifications ?? Array.Empty<GroupeSpecSaisi>())
                .Select(g => new GroupeDeSpecifications(
                    g.Name,
                    (g.Items ?? Array.Empty<LigneSpecSaisie>())
                        .Select(i => new SpecificationSaisie(i.Name, i.Value))
                        .ToList(),
                    g.DisplayOrder))
                .ToList());
    }

    private static Result<ProductCondition> ConstruireCondition(ConditionSaisie? saisie)
    {
        if (saisie is null)
        {
            // Voir l'encadré de ProductCondition.Neuf : c'est un choix, pas une absence.
            return ProductCondition.Neuf();
        }

        var type = Analyser<ProductConditionType>(saisie.Type, ProductConditionType.New,
            "catalog.condition.type_invalid", "État commercial inconnu.");
        if (type.IsFailure)
        {
            return Result.Failure<ProductCondition>(type.Error);
        }

        var fonctionnel = Analyser<ProductFunctionalStatus>(saisie.FunctionalStatus,
            ProductFunctionalStatus.FullyFunctional,
            "catalog.condition.functional_status_invalid", "État fonctionnel inconnu.");
        if (fonctionnel.IsFailure)
        {
            return Result.Failure<ProductCondition>(fonctionnel.Error);
        }

        RefurbisherType? reconditionneur = null;
        if (!string.IsNullOrWhiteSpace(saisie.RefurbishedByType))
        {
            var analyse = Analyser<RefurbisherType>(saisie.RefurbishedByType, RefurbisherType.Professional,
                "catalog.condition.refurbisher_type_invalid", "Type de reconditionneur inconnu.");
            if (analyse.IsFailure)
            {
                return Result.Failure<ProductCondition>(analyse.Error);
            }

            reconditionneur = analyse.Value;
        }

        var defauts = new List<DefautDeclare>();
        foreach (var defaut in saisie.Defects ?? Array.Empty<DefautSaisi>())
        {
            var typeDefaut = Analyser<ProductDefectType>(defaut.Type, ProductDefectType.Cosmetic,
                "catalog.condition.defect_type_invalid", "Type de défaut inconnu.");
            if (typeDefaut.IsFailure)
            {
                return Result.Failure<ProductCondition>(typeDefaut.Error);
            }

            var gravite = Analyser<ProductDefectSeverity>(defaut.Severity, ProductDefectSeverity.Minor,
                "catalog.condition.defect_severity_invalid", "Gravité de défaut inconnue.");
            if (gravite.IsFailure)
            {
                return Result.Failure<ProductCondition>(gravite.Error);
            }

            defauts.Add(new DefautDeclare(typeDefaut.Value, defaut.Location ?? string.Empty, defaut.Description, gravite.Value));
        }

        return ProductCondition.Create(
            type.Value,
            saisie.Grade,
            saisie.Description,
            saisie.HasOriginalPackaging,
            saisie.HasOriginalAccessories,
            fonctionnel.Value,
            defauts,
            reconditionneur,
            saisie.RefurbishedBySellerId,
            saisie.RefurbishmentOperations,
            saisie.BatteryHealthPercentage,
            saisie.BatteryReplaced);
    }

    /// <summary>
    /// Analyse une énumération en tolérant les formes de transport.
    ///
    /// « VERY_GOOD » ET « VeryGood » DÉSIGNENT LA MÊME CHOSE.
    ///
    /// Le cahier écrit les valeurs en SCREAMING_SNAKE_CASE dans les JSON (§9, §11)
    /// et en PascalCase dans le C# (§7). Sans ce retrait des soulignés, l'API
    /// refuserait exactement les valeurs que sa propre documentation donne en
    /// exemple — et le message d'erreur, « État commercial inconnu », désignerait
    /// une valeur que le lecteur vient de recopier du cahier.
    /// </summary>
    private static Result<TEnum> Analyser<TEnum>(string? valeur, TEnum defaut, string code, string message)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            return defaut;
        }

        var normalisee = valeur.Replace("_", string.Empty).Trim();

        return Enum.TryParse<TEnum>(normalisee, ignoreCase: true, out var analysee)
               && Enum.IsDefined(typeof(TEnum), analysee)
            ? analysee
            : Result.Failure<TEnum>(Error.Validation(code, $"{message} Valeur reçue : « {valeur} »."));
    }
}
