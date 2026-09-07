using System.Net;
using FluentAssertions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class HealthTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public HealthTests(GatewayFactory factory) => _factory = factory;

    /// <summary>CE TEST PROTÈGE DEUX CHOSES À LA FOIS.</summary>
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    public async Task Les_sondes_repondent_sans_authentification(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// L'aptitude ne dépend PAS de la santé des microservices : ici aucun n'est
    /// joignable, et la passerelle doit néanmoins se déclarer prête.
    /// </summary>
    [Fact]
    public async Task Ready_reste_vert_alors_qu_aucun_service_ne_repond()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
