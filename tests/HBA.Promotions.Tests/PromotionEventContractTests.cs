using System.Reflection;
using FluentAssertions;
using HBA.Promotions.Contracts;
using HBA.Promotions.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Promotions.Tests;

/// <summary>
/// Les trois événements du §10.16.
///
/// UN CONTRAT PUBLIC NE PRÉVIENT PAS QUAND IL CASSE.
///
/// Renommer un champ compile ; le consommateur d'en face ne lève rien non plus, il
/// désérialise un champ absent en valeur par défaut et continue. Un montant de
/// remise qui devient 0 en silence est exactement le genre de panne qu'on découvre
/// à la clôture du mois.
/// </summary>
public sealed class PromotionEventContractTests
{
    public static TheoryData<Type, string> EvenementsAttendus => new()
    {
        { typeof(PromotionCreatedIntegrationEvent), "promotion.created" },
        { typeof(PromotionExhaustedIntegrationEvent), "promotion.exhausted" },
        { typeof(CouponUsedIntegrationEvent), "coupon.used" }
    };

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_porte_le_nom_du_cahier_des_charges(Type type, string attendu)
    {
        var descripteur = type.GetCustomAttribute<HbaEventAttribute>();

        descripteur.Should().NotBeNull($"{type.Name} doit porter [HbaEvent]");
        descripteur!.EventType.Should().Be(attendu);
    }

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_est_versionne(Type type, string _)
        => type.GetCustomAttribute<HbaEventAttribute>()!.Version.Should().BePositive();

    /// <summary>
    /// LES MONTANTS SONT DES ENTIERS (§2), JAMAIS DES DÉCIMAUX.
    ///
    /// Le franc CFA n'a pas de sous-unité et le cahier impose des BIGINT. Un
    /// `decimal` dans un contrat public rouvrirait la porte aux arrondis que ce
    /// choix ferme — et un arrondi sur une remise, répété un million de fois,
    /// n'est plus une erreur d'arrondi.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Aucun_montant_n_est_decimal(Type type, string _)
    {
        var decimaux = type.GetProperties()
            .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
            .Select(p => p.Name);

        decimaux.Should().BeEmpty("le §2 impose des entiers pour toute somme d'argent");
    }

    /// <summary>
    /// `coupon.used` doit permettre de rapprocher un usage d'une commande ET d'un
    /// compte. Sans `OrderId`, le marketing compte des usages qu'il ne peut relier
    /// à aucune vente ; sans `UserId`, on ne peut pas répondre à « pourquoi mon
    /// coupon est-il consommé ».
    /// </summary>
    [Fact]
    public void L_usage_d_un_coupon_relie_la_commande_le_compte_et_le_montant()
    {
        var champs = typeof(CouponUsedIntegrationEvent).GetProperties().Select(p => p.Name).ToArray();

        champs.Should().Contain("OrderId");
        champs.Should().Contain("UserId");
        champs.Should().Contain("DiscountAmount");
        champs.Should().Contain("Code", "une réclamation arrive sous la forme « WELCOME10 », jamais sous celle d'un UUID");
    }

    /// <summary>
    /// `promotion.created` NE TRANSPORTE PAS LES RÈGLES D'ÉLIGIBILITÉ.
    ///
    /// Les recopier inviterait un consommateur à décider lui-même si un panier est
    /// éligible — et sa copie divergerait au premier ajout de condition. Pire : ce
    /// service refuse une règle qu'il ne sait pas évaluer, alors qu'un consommateur
    /// naïf ignorerait celle qu'il ne comprend pas, accordant la remise que la
    /// règle interdisait. L'éligibilité se demande à `EvaluatePromotion`.
    /// </summary>
    [Fact]
    public void La_creation_d_une_campagne_ne_transporte_pas_ses_regles()
    {
        var champs = typeof(PromotionCreatedIntegrationEvent).GetProperties().Select(p => p.Name);

        champs.Should().NotContain(nom => nom.Contains("Rule", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Le scope et le type voyagent sous la forme du cahier — `FOOD`,
    /// `FREE_DELIVERY` — et non sous celle de l'énumération C#. Le test vérifie le
    /// convertisseur, parce que c'est lui qui décide, pas la déclaration.
    /// </summary>
    [Theory]
    [InlineData("Global", "GLOBAL")]
    [InlineData("Marketplace", "MARKETPLACE")]
    [InlineData("Food", "FOOD")]
    [InlineData("Percent", "PERCENT")]
    [InlineData("Fixed", "FIXED")]
    [InlineData("FreeDelivery", "FREE_DELIVERY")]
    public void Les_valeurs_publiques_suivent_la_forme_du_cahier(string csharp, string attendu)
        => PromotionConstantes.Convertir(csharp).Should().Be(attendu);
}
