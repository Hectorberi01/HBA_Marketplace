using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>LE PARCOURS DU §28, DE BOUT EN BOUT, CONTRE UNE VRAIE BASE.</summary>
[Collection(CatalogIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class ParcoursProduitTests
{
    private readonly CatalogIntegrationFixture _fixture;

    public ParcoursProduitTests(CatalogIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>LE TEST QUI PROUVE QUE LE SCHÉMA SE CONSTRUIT À FROID.</summary>
    [Fact]
    public async Task Le_service_demarre_et_sert_la_vitrine_sur_une_base_neuve()
    {
        var reponse = await _fixture.CreateClient().GetAsync("/api/v1/catalog/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>LA FORME DE L'ENVELOPPE, QUI NE S'ÉPROUVE QU'ICI.</summary>
    [Fact]
    public async Task La_vitrine_repond_dans_l_enveloppe_du_paragraphe_25()
    {
        var corps = await _fixture.CreateClient().GetStringAsync("/api/v1/catalog/products");

        using var document = JsonDocument.Parse(corps);
        var racine = document.RootElement;

        racine.TryGetProperty("success", out var succes).Should().BeTrue(
            "toute réponse du service doit porter l'enveloppe du §25");
        succes.GetBoolean().Should().BeTrue();

        racine.TryGetProperty("data", out _).Should().BeTrue();

        racine.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("requestId", out var requestId).Should().BeTrue(
            "c'est l'identifiant que l'utilisateur cite dans un signalement");
        requestId.GetString().Should().NotBeNullOrWhiteSpace();

        // Liste paginée : la pagination vit dans `meta`, pas dans `data`.
        meta.TryGetProperty("page", out _).Should().BeTrue();
        meta.TryGetProperty("total", out _).Should().BeTrue();
    }

    /// <summary>L'ANCIEN CHEMIN DOIT RENDRE 404 SUR LE SERVICE, ET C'EST VOULU.</summary>
    [Fact]
    public async Task L_ancien_prefixe_n_est_plus_servi_par_le_service()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create());

        var reponse = await client.GetAsync("/api/catalog/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>LA SURFACE VENDEUR EXIGE LE RÔLE — VÉRIFIÉ ICI CONTRE UN HÔTE COMPLET.</summary>
    [Fact]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create());

        var reponse = await client.GetAsync("/api/v1/catalog/seller/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>LA DOCUMENTATION DOIT ÊTRE ATTEIGNABLE SANS JETON (lot 7).</summary>
    [Fact]
    public async Task La_documentation_openapi_est_servie_sans_jeton()
    {
        var reponse = await _fixture.CreateClient().GetAsync("/swagger/v1/swagger.json");

        reponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var document = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        document.TryGetProperty("paths", out var chemins).Should().BeTrue();

        chemins.EnumerateObject().Select(p => p.Name)
            .Should().Contain(chemin => chemin.StartsWith("/api/v1/catalog", StringComparison.Ordinal),
                "le document doit décrire les routes réelles du service");
    }
}
