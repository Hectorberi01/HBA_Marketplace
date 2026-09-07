using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.AuthorizationTests;

/// <summary>catalog-service : la vitrine doit s'afficher sans compte.</summary>
public sealed class CatalogPublicRoutesTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public CatalogPublicRoutesTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    /// <summary>
    /// L'assertion porte sur 401 et non sur 200 : sans base, la requête franchit
    /// l'autorisation puis échoue dans le handler.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/catalog/products")]
    [InlineData("/api/v1/catalog/products/iphone-16-pro")]
    [InlineData("/api/v1/catalog/categories")]
    [InlineData("/api/v1/catalog/brands")]
    public async Task La_vitrine_reste_lisible_en_anonyme(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>LA VUE DE GOUVERNANCE A ÉTÉ EXPOSÉE EN ANONYME, ET CE TEST L'INTERDIT.</summary>
    [Theory]
    [InlineData("/api/v1/catalog/admin/products")]
    public async Task La_vue_de_gouvernance_exige_un_role(string route)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>LA VALIDATION EST LA BARRIÈRE DU §4.</summary>
    [Theory]
    [InlineData("GET", "/api/v1/catalog/admin/products/reviews")]
    [InlineData("GET", "/api/v1/catalog/admin/products/{id}/review")]
    [InlineData("POST", "/api/v1/catalog/admin/products/{id}/approve")]
    [InlineData("POST", "/api/v1/catalog/admin/products/{id}/reject")]
    [InlineData("POST", "/api/v1/catalog/admin/products/{id}/suspend")]
    [InlineData("POST", "/api/v1/catalog/admin/products/{id}/restore")]
    public async Task La_validation_exige_un_role(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/catalog/admin/products/reviews")]
    public async Task La_file_de_validation_nest_pas_anonyme(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Le vendeur relit ses propres fiches par son groupe, pas par la vitrine.</summary>
    [Theory]
    [InlineData("/api/v1/catalog/seller/products")]
    [InlineData("/api/v1/catalog/seller/products/11111111-1111-1111-1111-111111111111")]
    public async Task Les_fiches_du_vendeur_exigent_un_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>UN JETON NE SUFFIT PLUS : LA SURFACE VENDEUR EXIGE LE RÔLE (§22).</summary>
    [Theory]
    [InlineData("GET", "/api/v1/catalog/seller/products")]
    [InlineData("POST", "/api/v1/catalog/seller/products")]
    [InlineData("POST", "/api/v1/catalog/seller/brands/requests")]
    [InlineData("POST", "/api/v1/catalog/seller/offers")]
    public async Task La_surface_vendeur_refuse_un_compte_sans_role(string methode, string route)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>LECTURE PUBLIQUE N'EST PAS ÉCRITURE PUBLIQUE.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/catalog/admin/brands")]
    [InlineData("POST", "/api/v1/catalog/admin/categories")]
    [InlineData("DELETE", "/api/v1/catalog/admin/categories/{id}")]
    public async Task Le_referentiel_exige_un_role(string methode, string gabarit)
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
