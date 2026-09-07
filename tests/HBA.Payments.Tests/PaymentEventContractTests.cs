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

    /// <summary>
    /// LE NOM DE CLASSE ET LE NOM MÉTIER DIVERGENT ICI, VOLONTAIREMENT.
    ///
    /// `PaymentCapturedIntegrationEvent` porte `payment.payment.succeeded`. Le
    /// domaine distingue l'autorisation de la capture — une nuance réelle chez les
    /// prestataires — là où le §10.12 ne connaît que « réussi ». C'est exactement ce
    /// que `[HbaEvent]` permet : le code garde son vocabulaire, le contrat garde le
    /// sien. Ce test fige cette correspondance pour qu'un renommage de classe ne la
    /// défasse pas en silence.
    /// </summary>
    [Fact]
    public void Le_paiement_encaisse_est_publie_sous_le_nom_succeeded()
    {
        typeof(PaymentCapturedIntegrationEvent)
            .GetCustomAttribute<HbaEventAttribute>()!.Action.Should().Be("succeeded");
    }

    /// <summary>
    /// SANS `OrderType`, `OrderId` EST AMBIGU ENTRE DEUX SERVICES.
    ///
    /// Marketplace et Food tiennent chacun leurs commandes, dans leur base, avec
    /// leurs identifiants. Un événement qui ne porte que `OrderId` oblige les deux à
    /// le chercher, et celui qui ne le trouve pas ne peut pas distinguer « pas pour
    /// moi » de « ma commande a disparu ».
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Tout_evenement_de_paiement_dit_de_quel_univers_vient_la_commande(Type type, string _)
    {
        type.GetProperties().Select(p => p.Name).Should().Contain("OrderType");
    }

    /// <summary>
    /// §8 : aucune donnée de carte brute. §19.7 : aucun secret dans un événement.
    /// Une référence prestataire sert à rejouer un appel chez le fournisseur — ce
    /// n'est l'affaire de personne d'autre que payment-service.
    /// </summary>
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// LOT 2 — `Provider`, `Amount` ET `Currency` SONT OPTIONNELS, ET ILS DOIVENT
    ///    LE RESTER.
    ///
    /// C'est toute la convention additive (D32) : un champ ajouté en `required`
    /// casse la désérialisation des messages DÉJÀ EN VOL dans l'outbox au moment
    /// du déploiement. Le symptôme est le pire qui soit — le consommateur lève
    /// sur un message qu'il ne pourra jamais accepter, et le rejeu le lui
    /// représente indéfiniment.
    ///
    /// Ce test ne vérifie pas que les champs existent : il vérifie qu'ils ne sont
    /// PAS obligatoires. C'est la seule des deux propriétés qui se casse en
    /// silence.
    /// ═════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `Provider` N'EST PAS `ProviderReference`, ET LA DISTINCTION EST TOUT.
    ///
    /// La liste des champs interdits ci-dessus contient `ProviderReference` — un
    /// identifiant qui sert à REJOUER un appel chez le prestataire, donc l'affaire
    /// de payment-service et de personne d'autre.
    ///
    /// `Provider` ne porte que le NOM du prestataire — « fedapay », « kkiapay ».
    /// Il ne permet aucun appel, il ne signe rien, et sans lui « quel fournisseur
    /// nous coûte des ventes ce mois-ci » n'a pas de réponse.
    ///
    /// Ce test existe pour qu'un futur relecteur qui verrait « Provider » dans un
    /// événement de paiement ne l'ajoute pas à la liste des interdits par réflexe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Le_nom_du_prestataire_est_permis_sa_reference_ne_l_est_pas(Type type, string _)
    {
        var proprietes = type.GetProperties().Select(propriete => propriete.Name).ToArray();

        proprietes.Should().NotContain("ProviderReference");
        proprietes.Should().NotContain("ProviderToken");
    }
}
