using System.Text.Json;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Domain.Promotions;

/// <summary>
/// Les types de règle que ce service sait évaluer (§10.16, colonne <c>
/// rule_type</c>).
/// </summary>
public static class PromotionRuleTypes
{
    /// <summary>Sous-total minimum du panier.</summary>
    public const string MinimumSubtotal = "MINIMUM_SUBTOTAL";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MinimumSubtotal };
}

/// <summary>UNE CONDITION D'ÉLIGIBILITÉ (§10.16, table <c>promotion_rules</c>).</summary>
public sealed class PromotionRule : Entity<Guid>
{
    private PromotionRule()
    {
        RuleType = string.Empty;
        RuleJson = string.Empty;
    }

    internal PromotionRule(Guid id, Guid promotionId, string ruleType, string ruleJson)
        : base(id)
    {
        PromotionId = promotionId;
        RuleType = ruleType;
        RuleJson = ruleJson;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid PromotionId { get; private set; }

    public string RuleType { get; private set; }

    /// <summary>Paramètres de la règle, tels que saisis.</summary>
    public string RuleJson { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Valide la forme d'une règle AU MOMENT DE LA CRÉATION.</summary>
    public static Result<PromotionRule> Create(Guid promotionId, string? ruleType, string? ruleJson)
    {
        var type = (ruleType ?? string.Empty).Trim().ToUpperInvariant();

        if (!PromotionRuleTypes.All.Contains(type))
        {
            return Result.Failure<PromotionRule>(Error.Validation(
                "promotions.rule.type_unknown",
                $"Type de règle « {ruleType} » inconnu. Connus : {string.Join(", ", PromotionRuleTypes.All)}."));
        }

        var json = string.IsNullOrWhiteSpace(ruleJson) ? "{}" : ruleJson.Trim();

        var sonde = new PromotionRule(Guid.NewGuid(), promotionId, type, json);

        // On évalue la règle sur un panier fictif : si ses paramètres sont
        // illisibles, autant l'apprendre à la création qu'au premier checkout.
        if (sonde.Evaluate(new PromotionContext(
                PromotionScope.Global, 1, 0, "XOF", Guid.Empty)).Error.Code is "promotions.rule.malformed")
        {
            return Result.Failure<PromotionRule>(Error.Validation(
                "promotions.rule.malformed",
                $"Paramètres illisibles pour la règle « {type} »."));
        }

        return sonde;
    }

    /// <summary>Le panier satisfait-il cette condition ?</summary>
    public Result Evaluate(PromotionContext context)
    {
        switch (RuleType.ToUpperInvariant())
        {
            case PromotionRuleTypes.MinimumSubtotal:
                if (!TryLireEntier("value", out var minimum))
                {
                    return Malformee();
                }

                return context.Subtotal >= minimum
                    ? Result.Success()
                    : Result.Failure(Error.BusinessRule(
                        "promotions.rule.minimum_subtotal",
                        $"Cette promotion demande un panier d'au moins {minimum} {context.Currency}."));

            default:
                // Voir l'encadré : on refuse, on n'ignore pas.
                return Result.Failure(Error.BusinessRule(
                    "promotions.rule.unsupported",
                    "Cette promotion comporte une condition que ce service ne sait pas évaluer."));
        }
    }

    private Result Malformee()
        => Result.Failure(Error.BusinessRule(
            "promotions.rule.malformed", "Les paramètres de cette promotion sont illisibles."));

    private bool TryLireEntier(string propriete, out long valeur)
    {
        valeur = 0;

        try
        {
            using var document = JsonDocument.Parse(RuleJson);

            return document.RootElement.TryGetProperty(propriete, out var element)
                   && element.TryGetInt64(out valeur);
        }
        catch (JsonException)
        {
            // Un `rule_json` illisible n'est pas rattrapable : il vient de la base,
            // pas d'une requête, donc personne n'est là pour le corriger.
            return false;
        }
    }
}
