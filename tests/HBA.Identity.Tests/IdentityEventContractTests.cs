using System.Reflection;
using FluentAssertions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>
/// Les trois événements du §10.1. Un nom d'événement est une chaîne : une faute de
/// frappe ne casse aucune compilation et se manifeste seulement par un consommateur
/// qui ne reçoit plus rien — indistinguable, dans un système asynchrone, de « il ne
/// s'est rien passé ».
/// </summary>
public sealed class IdentityEventContractTests
{
    public static TheoryData<Type, string> EvenementsAttendus => new()
    {
        { typeof(UserRegisteredIntegrationEvent), "identity.user.registered" },
        { typeof(UserLoggedInIntegrationEvent), "identity.user.logged_in" },
        { typeof(TokenRevokedIntegrationEvent), "identity.token.revoked" }
    };

    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Chaque_evenement_porte_le_nom_metier_du_cahier_des_charges(Type type, string attendu)
    {
        var descriptor = type.GetCustomAttribute<HbaEventAttribute>();

        descriptor.Should().NotBeNull();
        descriptor!.EventType.Should().Be(attendu);
        descriptor.Domain.Should().Be("identity");
    }

    /// <summary>
    /// AUCUN SECRET NI DONNÉE PERSONNELLE DANS UN ÉVÉNEMENT (§19.7).
    ///
    /// Un événement se pose sur un topic conservé plusieurs jours et se retrouve dans
    /// les journaux de chaque consommateur. Un jeton y vaut une session volée ; une
    /// adresse IP est une donnée personnelle au sens du RGPD. Le jour où quelqu'un
    /// ajoutera l'un ou l'autre « pour faciliter la détection de fraude », ce test
    /// tombera.
    /// </summary>
    [Theory]
    [MemberData(nameof(EvenementsAttendus))]
    public void Aucun_evenement_ne_transporte_de_secret_ni_de_donnee_personnelle(Type type, string _)
    {
        var interdits = new[]
        {
            "Password", "PasswordHash", "Token", "AccessToken", "RefreshToken",
            "SecurityStamp", "CodeHash", "IpAddress", "UserAgent"
        };

        type.GetProperties().Select(p => p.Name).Should().NotIntersectWith(interdits);
    }
}
