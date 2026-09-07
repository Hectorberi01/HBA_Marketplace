using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.AuthorizationTests;

/// <summary>Les routes ouvertes en phase 3 et par les tâches #179 / #230.</summary>
public sealed class OfferAndVariantGuardTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public OfferAndVariantGuardTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    private static string Route(string gabarit) => gabarit
        .Replace("{id}", Guid.NewGuid().ToString())
        .Replace("{variantId}", Guid.NewGuid().ToString())
        .Replace("{storeId}", Guid.NewGuid().ToString())
        .Replace("{mediaId}", Guid.NewGuid().ToString());

    /// <summary>Les vingt-deux routes vendeur refusent un appel SANS JETON.</summary>
    [Theory]
    // ── Offres (phase 3) ──────────────────────────────────────────────────
    [InlineData("POST", "/api/v1/catalog/seller/offers")]
    [InlineData("GET", "/api/v1/catalog/seller/stores/{storeId}/offers")]
    [InlineData("PUT", "/api/v1/catalog/seller/offers/{id}/price")]
    [InlineData("PUT", "/api/v1/catalog/seller/offers/{id}/handling-time")]
    [InlineData("PUT", "/api/v1/catalog/seller/offers/{id}/promotion")]
    [InlineData("DELETE", "/api/v1/catalog/seller/offers/{id}/promotion")]
    [InlineData("POST", "/api/v1/catalog/seller/offers/{id}/activate")]
    [InlineData("POST", "/api/v1/catalog/seller/offers/{id}/pause")]
    [InlineData("DELETE", "/api/v1/catalog/seller/offers/{id}")]
    // ── Produits (#179) ───────────────────────────────────────────────────
    [InlineData("POST", "/api/v1/catalog/seller/products")]
    [InlineData("PUT", "/api/v1/catalog/seller/products/{id}")]
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/status")]
    [InlineData("DELETE", "/api/v1/catalog/seller/products/{id}")]
    [InlineData("PUT", "/api/v1/catalog/seller/products/{id}/tags")]
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/variants")]
    [InlineData("PUT", "/api/v1/catalog/seller/products/{id}/variants/{variantId}")]
    [InlineData("DELETE", "/api/v1/catalog/seller/products/{id}/variants/{variantId}")]
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/media")]
    [InlineData("DELETE", "/api/v1/catalog/seller/products/{id}/media/{mediaId}")]
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/media/{mediaId}/primary")]
    [InlineData("PUT", "/api/v1/catalog/seller/products/{id}/media/order")]
    // ── Déclinaison désactivable (#230) et détourage ───────────────────────
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/variants/{variantId}/status")]
    [InlineData("POST", "/api/v1/catalog/seller/products/images/process")]
    public async Task Sans_jeton_le_groupe_vendeur_refuse(string methode, string gabarit)
    {
        var response = await Requetes.EnvoyerAsync(
            _factory.CreateClient(), methode, Route(gabarit));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>La vitrine PUBLIQUE ne doit pas rendre les routes vendeur atteignables.</summary>
    [Fact]
    public async Task La_liste_des_offres_d_une_boutique_n_est_pas_anonyme()
    {
        var response = await _factory.CreateClient()
            .GetAsync(Route("/api/v1/catalog/seller/stores/{storeId}/offers"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Un jeton d'ACHETEUR ne franchit pas les routes vendeur au-delà de la garde.
    /// </summary>
    /// <remarks>
    /// LE JETON PORTE MAINTENANT LE RÔLE `Seller`, ET LA PRÉMISSE A CHANGÉ.
    ///
    /// L'ancienne version passait un jeton SANS rôle et n'affirmait que « pas
    /// 401 » — parce que le groupe vendeur n'exigeait alors qu'un jeton. Depuis le
    /// lot 6 il exige `Seller`, `Admin` ou `Moderator` : un jeton nu reçoit un 403,
    /// qui n'est toujours pas 401. Le test serait resté VERT en ne prouvant plus
    /// rien — l'assertion aurait survécu à la disparition de son sujet.
    ///
    /// Ce qu'il vérifie désormais : un vendeur LÉGITIME n'est pas enfermé dehors.
    /// C'est le vrai risque de cette bascule, et il est déjà arrivé dans ce dépôt —
    /// un `[Authorize(PartnerOnly)]` posé sous un `MerchantOnly` de classe
    /// additionne les exigences au lieu de les remplacer, et le restaurateur restait
    /// dehors. `RequireRole(Seller, Admin, Moderator)` combiné à une politique de
    /// groupe plus étroite produirait exactement cela.
    ///
    /// L'assertion reste « ni 401 ni 403 » et non « 200 » : au-delà de
    /// l'autorisation, `DenyUnlessProductOwnerAsync` a besoin de merchant-service et
    /// d'une base. Le refus 404 sur la fiche d'autrui appartient aux tests
    /// d'intégration.
    /// </remarks>
    [Theory]
    [InlineData("POST", "/api/v1/catalog/seller/products")]
    [InlineData("POST", "/api/v1/catalog/seller/offers")]
    [InlineData("POST", "/api/v1/catalog/seller/products/{id}/variants/{variantId}/status")]
    public async Task Un_vendeur_legitime_n_est_refoule_ni_en_401_ni_en_403(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));

        var response = await Requetes.EnvoyerAsync(client, methode, Route(gabarit));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>ET LE PENDANT : UN ACHETEUR N'ENTRE PLUS DU TOUT.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/catalog/seller/products")]
    [InlineData("POST", "/api/v1/catalog/seller/offers")]
    [InlineData("GET", "/api/v1/catalog/seller/stores/{storeId}/offers")]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await Requetes.EnvoyerAsync(client, methode, Route(gabarit));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>L'administration du référentiel reste fermée à un compte sans rôle.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/catalog/admin/brands")]
    [InlineData("POST", "/api/v1/catalog/admin/categories")]
    public async Task L_administration_du_referentiel_exige_un_role(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await Requetes.EnvoyerAsync(client, methode, Route(gabarit));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
