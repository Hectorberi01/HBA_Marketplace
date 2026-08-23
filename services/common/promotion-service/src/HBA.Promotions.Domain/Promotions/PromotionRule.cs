using System.Text.Json;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Promotions.Domain.Promotions;

/// <summary>Les types de règle que ce service sait évaluer (§10.16, colonne <c>rule_type</c>).</summary>
public static class PromotionRuleTypes
{
    /// <summary>Sous-total minimum du panier. <c>{"value": 5000}</c>.</summary>
    public const string MinimumSubtotal = "MINIMUM_SUBTOTAL";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MinimumSubtotal };
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE CONDITION D'ÉLIGIBILITÉ (§10.16, table <c>promotion_rules</c>).
///
/// Le cahier stocke <c>rule_type</c> + <c>rule_json</c> : une porte ouverte, qui
/// permet d'ajouter une condition sans migration. C'est commode, et c'est
/// exactement ce qui rend la règle suivante indispensable.
///
/// UN TYPE DE RÈGLE INCONNU REFUSE LA PROMOTION. IL NE L'IGNORE PAS.
///
/// C'est le seul choix défendable, et l'intuition va dans l'autre sens — « on ne
/// sait pas évaluer, donc on laisse passer » paraît tolérant. Il ne l'est pas :
/// une règle existe pour RESTREINDRE. L'ignorer accorde précisément la remise que
/// quelqu'un avait écrit une règle pour empêcher.
///
/// Le scénario est concret. Une campagne est créée depuis une interface plus
/// récente que ce service, avec une condition qu'il ne connaît pas encore —
/// « premier achat uniquement », « catégorie X exclue ». En ignorant, la remise
/// part sur tous les paniers, et personne ne s'en aperçoit avant la clôture du
/// mois. En refusant, la campagne ne s'applique à personne : c'est visible en
/// une heure, et ça ne coûte rien d'autre qu'un déploiement.
///
/// Échouer FERMÉ coûte une campagne inactive ; échouer ouvert coûte un budget.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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

    /// <summary>Paramètres de la règle, tels que saisis. Voir <see cref="PromotionRuleTypes"/>.</summary>
    public string RuleJson { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>
    /// Valide la forme d'une règle AU MOMENT DE LA CRÉATION.
    ///
    /// ON REFUSE LA CAMPAGNE, PAS LA RÈGLE.
    ///
    /// Accepter une campagne en écartant les règles illisibles produirait une
    /// promotion moins restrictive que ce que son auteur a demandé — et il n'aurait
    /// aucun moyen de le voir : l'écran afficherait la campagne comme créée.
    /// </summary>
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

    /// <summary>
    /// Le panier satisfait-il cette condition ?
    ///
    /// Rend un code distinct par raison : « ajoutez 2 000 F » est actionnable,
    /// « ce coupon ne s'applique pas » ne l'est pas.
    /// </summary>
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
