using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Merchants.AuthorizationTests;

/// <summary>merchant-service : la gouvernance d'un vendeur n'appartient pas au vendeur.</summary>
public sealed class MerchantsAuthorizationTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public MerchantsAuthorizationTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    /// <summary>`GET /` EST DANS CETTE LISTE, ET CE N'EST PAS UN EXCÈS DE ZÈLE.</summary>
    [Theory]
    [InlineData("GET", "/api/v1/merchants/")]
    [InlineData("POST", "/api/v1/merchants/{id}/kyb/approve")]
    [InlineData("POST", "/api/v1/merchants/{id}/kyb/reject")]
    [InlineData("POST", "/api/v1/merchants/{id}/activate")]
    [InlineData("POST", "/api/v1/merchants/{id}/suspend")]
    [InlineData("POST", "/api/v1/merchants/{id}/lift-suspension")]
    [InlineData("POST", "/api/v1/merchants/{id}/reactivation/approve")]
    [InlineData("DELETE", "/api/v1/merchants/{id}")]
    public async Task Un_compte_sans_role_ne_gouverne_pas_un_vendeur(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>SUSPENDRE UNE BOUTIQUE EST UNE SANCTION, PAS UNE FERMETURE.</summary>
    [Theory]
    [InlineData("/suspend")]
    [InlineData("/lift-suspension")]
    public async Task Un_compte_sans_role_ne_sanctionne_pas_une_boutique(string suffixe)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = $"/api/v1/merchants/{Guid.NewGuid()}/stores/{Guid.NewGuid()}{suffixe}";

        var response = await Requetes.EnvoyerAsync(client, "POST", route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>L'INSCRIPTION RESTE OUVERTE À TOUT COMPTE AUTHENTIFIÉ.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/merchants/")]
    [InlineData("GET", "/api/v1/merchants/me")]
    public async Task L_inscription_vendeur_reste_ouverte_a_tout_compte(string methode, string route)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>ET LE PENDANT : LE RESTE DE LA SURFACE VENDEUR REFUSE UN ACHETEUR.</summary>
    [Theory]
    [InlineData("PUT", "/api/v1/merchants/{id}/profile")]
    [InlineData("PUT", "/api/v1/merchants/{id}/payout-account")]
    [InlineData("POST", "/api/v1/merchants/{id}/kyb/documents")]
    [InlineData("POST", "/api/v1/merchants/{id}/kyb/submit")]
    [InlineData("POST", "/api/v1/merchants/{id}/close")]
    [InlineData("GET", "/api/v1/merchants/{id}/stores/")]
    [InlineData("POST", "/api/v1/merchants/{id}/stores/")]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Un vendeur LÉGITIME n'est pas enfermé dehors. C'est le vrai risque de cette
    /// bascule, et il s'est déjà produit dans ce dépôt : un `[Authorize]` posé sous
    /// un autre additionne les exigences au lieu de les remplacer.
    ///
    /// « Ni 401 ni 403 » et non « 200 » : au-delà de l'autorisation,
    /// `DenyUnlessOwnSellerAsync` a besoin de merchant-service et d'une base.
    /// </summary>
    [Theory]
    [InlineData("PUT", "/api/v1/merchants/{id}/profile")]
    [InlineData("POST", "/api/v1/merchants/{id}/kyb/submit")]
    public async Task Un_vendeur_legitime_n_est_refoule_ni_en_401_ni_en_403(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Sans `AllowAnonymous`, Docker redémarre le conteneur en boucle.</summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/v1/merchants/")]
    [InlineData("/api/v1/merchants/me")]
    public async Task Les_routes_protegees_rendent_401_sans_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
