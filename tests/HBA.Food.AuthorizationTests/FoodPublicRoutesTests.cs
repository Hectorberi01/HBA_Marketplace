using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Food.AuthorizationTests;

/// <summary>
/// food-service : la vitrine reste anonyme, la modération exige un rôle.
/// </summary>
/// <remarks>
/// LE GROUPE `/api/food` EST UN `MapGroup` NU : la politique de repli du socle
/// s'y applique. Ces routes ne survivent que par leur `AllowAnonymous` explicite.
/// Sans elles, un visiteur non connecté reçoit 401 sur la liste des restaurants —
/// c'est-à-dire que HBA Food n'a plus de vitrine.
/// </remarks>
public sealed class FoodPublicRoutesTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public FoodPublicRoutesTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    /// <summary>
    /// L'assertion porte sur 401 et non sur 200 : sans base, la requête franchit
    /// l'autorisation puis échoue dans le handler.
    /// </summary>
    [Fact]
    public async Task La_vitrine_reste_lisible_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/api/food/restaurants");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// `restaurants/pending` A VÉCU DANS LE GROUPE PARTENAIRE.
    ///
    /// La file des dossiers en attente et les décisions de modération n'ont de
    /// sens signées par personne d'autre que la plateforme : un restaurateur qui
    /// approuve sa propre candidature n'est pas une candidature.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/food/admin/restaurants/pending")]
    [InlineData("POST", "/api/food/admin/restaurants/{id}/approve")]
    [InlineData("POST", "/api/food/admin/restaurants/{id}/suspend")]
    public async Task La_moderation_exige_un_role(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
