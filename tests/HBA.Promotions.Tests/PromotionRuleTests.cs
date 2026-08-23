using FluentAssertions;
using HBA.Promotions.Domain.Promotions;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// Les conditions d'éligibilité du §10.16 (<c>promotion_rules</c>).
///
/// Le couple <c>rule_type</c> + <c>rule_json</c> permet d'ajouter une condition
/// sans migration. C'est commode, et c'est ce qui rend le comportement en cas de
/// type inconnu déterminant.
/// </summary>
public sealed class PromotionRuleTests
{
    private static readonly DateTime Maintenant = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static PromotionContext Panier(long sousTotal)
        => new(PromotionScope.Marketplace, sousTotal, 1_000, "XOF", Guid.NewGuid());

    private static Promotion Campagne()
        => Promotion.Create(
            "Rentrée", PromotionScope.Global, PromotionType.Percent, 10,
            Maintenant.AddDays(-1), Maintenant.AddDays(10), 100_000).Value;

    // ────────────────────────────────────────────────────── Sous-total minimum

    [Fact]
    public void Un_panier_sous_le_minimum_est_refuse_avec_un_message_actionnable()
    {
        var campagne = Campagne();
        campagne.AddRule(PromotionRuleTypes.MinimumSubtotal, """{"value": 5000}""").IsSuccess.Should().BeTrue();

        var resultat = campagne.EnsureApplicable(Panier(3_000), Maintenant);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.rule.minimum_subtotal");
        resultat.Error.Message.Should().Contain("5000", "le client doit savoir combien il lui manque");
    }

    [Fact]
    public void Un_panier_au_minimum_exact_passe()
    {
        var campagne = Campagne();
        campagne.AddRule(PromotionRuleTypes.MinimumSubtotal, """{"value": 5000}""");

        campagne.EnsureApplicable(Panier(5_000), Maintenant).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Une_campagne_sans_regle_reste_applicable()
        => Campagne().EnsureApplicable(Panier(10), Maintenant).IsSuccess.Should().BeTrue();

    // ─────────────────────────────────────────────────────────── Type inconnu

    /// <summary>
    /// LE TEST LE PLUS IMPORTANT DU FICHIER, ET L'INTUITION VA DANS L'AUTRE SENS.
    ///
    /// « On ne sait pas évaluer, donc on laisse passer » paraît tolérant. Il ne
    /// l'est pas : une règle existe pour RESTREINDRE, et l'ignorer accorde
    /// exactement la remise que quelqu'un avait écrit une règle pour empêcher.
    ///
    /// Échouer fermé coûte une campagne inactive — visible en une heure. Échouer
    /// ouvert coûte un budget, et ne se voit qu'à la clôture du mois.
    /// </summary>
    [Fact]
    public void Une_regle_de_type_inconnu_refuse_la_promotion_au_lieu_de_l_ignorer()
    {
        // On construit la règle directement : `AddRule` refuse un type inconnu, et
        // c'est bien le but — mais une campagne créée par une version plus récente
        // du service, elle, arrive de la base avec ce type.
        var regle = ReglePersistee("FIRST_ORDER_ONLY", "{}");

        var verdict = regle.Evaluate(Panier(10_000));

        verdict.IsFailure.Should().BeTrue("ignorer une restriction inconnue accorde la remise qu'elle interdisait");
        verdict.Error.Code.Should().Be("promotions.rule.unsupported");
    }

    [Fact]
    public void Un_type_de_regle_inconnu_est_refuse_des_la_creation()
    {
        var resultat = Campagne().AddRule("FIRST_ORDER_ONLY", "{}");

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.rule.type_unknown");
    }

    // ──────────────────────────────────────────────────── Paramètres illisibles

    [Fact]
    public void Un_json_de_regle_illisible_refuse_la_promotion()
    {
        var regle = ReglePersistee(PromotionRuleTypes.MinimumSubtotal, "{ceci n'est pas du json");

        regle.Evaluate(Panier(10_000)).Error.Code.Should().Be("promotions.rule.malformed");
    }

    [Fact]
    public void Un_minimum_sans_valeur_est_refuse_des_la_creation()
    {
        var resultat = Campagne().AddRule(PromotionRuleTypes.MinimumSubtotal, "{}");

        resultat.IsFailure.Should().BeTrue("un minimum sans montant ne veut rien dire");
        resultat.Error.Code.Should().Be("promotions.rule.malformed");
    }

    [Fact]
    public void Le_type_de_regle_est_normalise_en_majuscules()
    {
        var campagne = Campagne();

        campagne.AddRule("minimum_subtotal", """{"value": 1000}""").IsSuccess.Should().BeTrue();
        campagne.Rules.Single().RuleType.Should().Be(PromotionRuleTypes.MinimumSubtotal);
    }

    // ──────────────────────────────────────────────────── Moment de l'ajout

    /// <summary>
    /// RESTREINDRE UNE CAMPAGNE ACTIVE CHANGE LA RÈGLE SOUS LES PIEDS DU CLIENT.
    ///
    /// Celui qui a rempli son panier pour atteindre un minimum qui n'existait pas
    /// hier ne comprendra pas le refus, et le support non plus.
    /// </summary>
    [Fact]
    public void Une_campagne_deja_demarree_refuse_une_nouvelle_condition()
    {
        var campagne = Campagne();
        campagne.ConsumeBudget(1_000);   // passe en Active

        var resultat = campagne.AddRule(PromotionRuleTypes.MinimumSubtotal, """{"value": 5000}""");

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("promotions.rule.campaign_started");
    }

    /// <summary>
    /// Une règle refusée ne laisse rien derrière elle : la campagne ne doit pas se
    /// retrouver avec une condition à moitié posée.
    /// </summary>
    [Fact]
    public void Une_regle_refusee_n_est_pas_ajoutee()
    {
        var campagne = Campagne();

        campagne.AddRule("INCONNU", "{}");

        campagne.Rules.Should().BeEmpty();
    }

    /// <summary>
    /// Une règle telle qu'EF la matérialise depuis la base, sans passer par
    /// <c>Create</c> — c'est exactement le cas qu'on veut couvrir : une campagne
    /// écrite par une version plus récente du service.
    /// </summary>
    private static PromotionRule ReglePersistee(string type, string json)
        => new(Guid.NewGuid(), Guid.NewGuid(), type, json);
}
