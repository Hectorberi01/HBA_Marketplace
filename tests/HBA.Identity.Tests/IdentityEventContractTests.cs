using System.Reflection;
using FluentAssertions;
using HBA.Identity.Contracts.IntegrationEvents;
using HBA.Shared.IntegrationEvents;
using Xunit;

namespace HBA.Identity.Tests;

/// <summary>Les trois événements du §10.1.</summary>
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

    /// <summary>AUCUN SECRET NI DONNÉE PERSONNELLE DANS UN ÉVÉNEMENT (§19.7).</summary>
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
