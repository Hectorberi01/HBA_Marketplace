using System.Reflection;
using FluentAssertions;
using HBA.Shared.IntegrationEvents;
using HBA.Users.Contracts.IntegrationEvents;
using Xunit;

namespace HBA.Users.Tests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES CONTRATS D'ÉVÉNEMENT SONT TESTÉS, PARCE QU'ILS NE COMPILENT PAS.
///
/// Un nom d'événement Kafka est une chaîne. Se tromper de segment — `user.profile.update`
/// au lieu de `user.profile.updated` — ne provoque AUCUNE erreur de compilation, aucune
/// exception à l'exécution côté producteur, et se manifeste uniquement par un consommateur
/// qui ne reçoit plus rien. Dans un système asynchrone, « ne reçoit plus rien » est
/// indistinguable de « il ne s'est rien passé ».
///
/// Ces tests sont donc la seule barrière entre une faute de frappe et un silence.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UserEventContractTests
{
    public static TheoryData<Type, string> EvenementsAttendus => new()
    {
        { typeof(UserProfileChangedIntegrationEvent), "user.profile.updated" },
        { typeof(UserAddressCreatedIntegrationEvent), "user.address.created" },
        { typeof(UserDeviceRegisteredIntegrationEvent), "user.device.registered" }
    };

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_porte_le_nom_metier_du_cahier_des_charges(Type type, string attendu)
    {
        var descriptor = type.GetCustomAttribute<HbaEventAttribute>();

        descriptor.Should().NotBeNull(
            "un événement sans [HbaEvent] est publié à l'ancienne et n'atteint aucun consumer conforme");
        descriptor!.EventType.Should().Be(attendu);
    }

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_declare_un_domaine_et_une_version(Type type, string _)
    {
        var descriptor = type.GetCustomAttribute<HbaEventAttribute>()!;

        descriptor.Domain.Should().Be("user");
        descriptor.Version.Should().BeGreaterThan(0);
        descriptor.AggregateType.Should().NotBeNullOrWhiteSpace(
            "aggregate.type de l'enveloppe §19.1 en dépend");
    }

    /// <summary>
    /// CE TEST GARDE UNE RÈGLE DE CONFIDENTIALITÉ, PAS UNE CONVENTION DE NOMMAGE.
    ///
    /// Le §19.7 interdit les données personnelles inutiles dans les événements. Un
    /// jeton push vaut envoi de notification à qui le détient ; une adresse postale
    /// et un point GPS sont des données personnelles. Les uns comme les autres se
    /// retrouveraient sur un topic conservé plusieurs jours et dans les journaux de
    /// chaque consommateur.
    ///
    /// Le jour où quelqu'un ajoutera `PushToken` « pour éviter un appel gRPC », ce
    /// test tombera.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Aucun_evenement_ne_transporte_de_donnee_sensible(Type type, string _)
    {
        var interdits = new[] { "PushToken", "Street", "Line1", "Latitude", "Longitude", "Phone", "Email" };

        var proprietes = type.GetProperties().Select(p => p.Name).ToArray();

        proprietes.Should().NotIntersectWith(interdits);
    }
}
