using System.Reflection;
using FluentAssertions;
using HBA.Financial.Payments.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Payments.Tests;

/// <summary>Les quatre événements du §10.12.</summary>
public sealed class PaymentEventContractTests
{
    public static TheoryData<Type, string> EvenementsAttendus => new()
    {
        { typeof(PaymentCreatedIntegrationEvent), "payment.created" },
        { typeof(PaymentCapturedIntegrationEvent), "payment.succeeded" },
        { typeof(PaymentFailedIntegrationEvent), "payment.failed" },
        { typeof(PaymentRefundedIntegrationEvent), "payment.refunded" },
        { typeof(PaymentRefundFailedIntegrationEvent), "payment.refund.failed" }
    };

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_porte_le_nom_metier_du_cahier_des_charges(Type type, string attendu)
    {
        var descriptor = type.GetCustomAttribute<HbaEventAttribute>();

        descriptor.Should().NotBeNull();
        descriptor!.EventType.Should().Be(attendu);
    }

    /// <summary>LE NOM DE CLASSE ET LE NOM MÉTIER DIVERGENT ICI, VOLONTAIREMENT.</summary>
    [Fact]
    public void Le_paiement_encaisse_est_publie_sous_le_nom_succeeded()
    {
        typeof(PaymentCapturedIntegrationEvent)
            .GetCustomAttribute<HbaEventAttribute>()!.Action.Should().Be("succeeded");
    }

    /// <summary>SANS `OrderType`, `OrderId` EST AMBIGU ENTRE DEUX SERVICES.</summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Tout_evenement_de_paiement_dit_de_quel_univers_vient_la_commande(Type type, string _)
    {
        type.GetProperties().Select(p => p.Name).Should().Contain("OrderType");
    }

    /// <summary>§8 : aucune donnée de carte brute.</summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Aucun_evenement_ne_transporte_de_donnee_de_paiement_sensible(Type type, string _)
    {
        var interdits = new[]
        {
            "CardNumber", "Pan", "Cvv", "ExpiryMonth", "ExpiryYear",
            "ProviderReference", "ProviderToken", "ApiKey", "Signature"
        };

        type.GetProperties().Select(p => p.Name).Should().NotIntersectWith(interdits);
    }

    /// <summary>
    /// LOT 2 — `Provider`, `Amount` ET `Currency` SONT OPTIONNELS, ET ILS DOIVENT
    /// LE RESTER.
    /// </summary>
    [Theory]
    [InlineData(typeof(PaymentCapturedIntegrationEvent))]
    [InlineData(typeof(PaymentFailedIntegrationEvent))]
    public void Les_trois_champs_ajoutes_au_lot_2_sont_optionnels(Type type)
    {
        foreach (var nom in new[] { "Provider", "Amount", "Currency" })
        {
            var propriete = type.GetProperty(nom);

            propriete.Should().NotBeNull($"« {nom} » débloque les graphes de paiement du lot 2");

            propriete!.GetCustomAttributes()
                .Select(attribut => attribut.GetType().Name)
                .Should().NotContain(
                    "RequiredMemberAttribute",
                    $"« {nom} » doit rester optionnel : un message en vol ne le porte pas");
        }
    }

    /// <summary>`Provider` N'EST PAS `ProviderReference`, ET LA DISTINCTION EST TOUT.</summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Le_nom_du_prestataire_est_permis_sa_reference_ne_l_est_pas(Type type, string _)
    {
        var proprietes = type.GetProperties().Select(propriete => propriete.Name).ToArray();

        proprietes.Should().NotContain("ProviderReference");
        proprietes.Should().NotContain("ProviderToken");
    }
}
