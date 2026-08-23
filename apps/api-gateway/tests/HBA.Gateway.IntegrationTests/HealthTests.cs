using System.Net;
using FluentAssertions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class HealthTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public HealthTests(GatewayFactory factory) => _factory = factory;

    /// <summary>
    /// CE TEST PROTÈGE DEUX CHOSES À LA FOIS.
    ///
    /// D'une part que la sonde réponde ; d'autre part qu'elle réponde SANS jeton.
    /// La politique de repli exige un utilisateur authentifié : sans
    /// `AllowAnonymous`, Docker recevrait 401, déclarerait le conteneur malsain
    /// et le redémarrerait en boucle. Le symptôme — un conteneur qui redémarre
    /// sans erreur applicative — n'oriente vers aucune cause évidente.
    /// </summary>
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
    /// joignable, et la passerelle doit néanmoins se déclarer prête. C'est le
    /// garde-fou contre la panne en cascade décrite dans
    /// <c>ProxyConfigurationHealthCheck</c>.
    /// </summary>
    [Fact]
    public async Task Ready_reste_vert_alors_qu_aucun_service_ne_repond()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
