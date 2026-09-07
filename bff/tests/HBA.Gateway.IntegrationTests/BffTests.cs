using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class BffTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public BffTests(GatewayFactory factory) => _factory = factory;

    /// <summary>
    /// Aucune section n'est configurée : l'écran doit rendre une réponse vide et
    /// valide, PAS une erreur. C'est l'état normal tant qu'aucun service n'est
    /// déployé, et une 500 ici mettrait la passerelle en échec dans les tableaux
    /// de bord tout en masquant les vraies pannes.
    /// </summary>
    [Theory]
    [InlineData("/api/bff/client/express/home", "express")]
    [InlineData("/api/bff/client/food/home", "food")]
    public async Task Un_ecran_sans_section_configuree_rend_une_reponse_vide(string route, string surface)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        document.RootElement.GetProperty("surface").GetString().Should().Be(surface);
        document.RootElement.GetProperty("sections").GetArrayLength().Should().Be(0);
        document.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// LES DEUX UNIVERS NE DOIVENT PAS PARTAGER DE POINT DE TERMINAISON.
    ///
    /// Le champ `surface` permet au client de constater immédiatement une
    /// confusion, plutôt que de la découvrir quand un produit s'affiche dans un
    /// menu de restaurant.
    /// </summary>
    [Fact]
    public async Task Les_deux_univers_rendent_des_surfaces_distinctes()
    {
        var client = _factory.CreateClient();

        var express = await client.GetStringAsync("/api/bff/client/express/home");
        var food = await client.GetStringAsync("/api/bff/client/food/home");

        express.Should().Contain("\"express\"").And.NotContain("\"food\"");
        food.Should().Contain("\"food\"").And.NotContain("\"express\"");
    }
}
