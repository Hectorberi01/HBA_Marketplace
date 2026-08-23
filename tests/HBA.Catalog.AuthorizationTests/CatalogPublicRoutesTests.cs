using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.AuthorizationTests;

/// <summary>
/// catalog-service : la vitrine doit s'afficher sans compte.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE LA `FallbackPolicy` PEUT CASSER SANS QUE PERSONNE NE LE VOIE.
///
/// Le socle installe désormais une politique de repli qui ferme tout point de
/// terminaison sans métadonnée d'autorisation (voir ServiceHostExtensions). Les
/// routes de vitrine ne survivent que par leur `AllowAnonymous` EXPLICITE : le
/// groupe `/api/v1/catalog` est un `MapGroup` nu, donc soumis au repli.
///
/// Retirer un seul de ces `AllowAnonymous`, et la page d'accueil de l'application
/// cliente rend 401 pour un visiteur non connecté — c'est-à-dire pour la totalité
/// des nouveaux venus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class CatalogPublicRoutesTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public CatalogPublicRoutesTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    /// <summary>
    /// L'assertion porte sur 401 et non sur 200 : sans base, la requête franchit
    /// l'autorisation puis échoue dans le handler. C'est le franchissement qui
    /// est éprouvé — voir AuthorizationTestFactory.
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA VUE DE GOUVERNANCE A ÉTÉ EXPOSÉE EN ANONYME, ET CE TEST L'INTERDIT.
    ///
    /// `GET /api/v1/catalog/products` était branchée sur `ListAllProductsQuery` —
    /// documentée « console admin » — dont le filtre de statut est FACULTATIF.
    /// Sans paramètre, la vitrine rendait les brouillons, les fiches en attente de
    /// validation, les rejetées et les suspendues, plus la répartition du
    /// catalogue par statut.
    ///
    /// La requête a déménagé sous `/admin`. Ce test échoue si elle en ressort :
    /// c'est la seule barrière qui ne dépend pas de la lecture d'un commentaire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Theory]
    [InlineData("/api/v1/catalog/admin/products")]
    public async Task La_vue_de_gouvernance_exige_un_role(string route)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA VALIDATION EST LA BARRIÈRE DU §4. ELLE NE PEUT PAS S'OUVRIR À UN
    ///    COMPTE ORDINAIRE.
    ///
    /// « Un vendeur ne peut jamais publier un produit qui n'a pas été approuvé par
    /// un administrateur. » Cette règle ne tient que si l'approbation elle-même est
    /// hors de portée du vendeur. Un jeton d'acheteur — celui que délivre n'importe
    /// quelle inscription — doit se heurter à un 403 sur les six routes.
    ///
    /// Le domaine refuserait déjà l'enchaînement dans la plupart des cas ; ce test
    /// ne dépend pas de cette chance-là.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// Le vendeur relit ses propres fiches par son groupe, pas par la vitrine.
    /// Ces deux routes remplacent l'accès qu'il avait aux brouillons via la route
    /// publique — voir l'encadré de `MapCatalogEndpoints`.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/catalog/seller/products")]
    [InlineData("/api/v1/catalog/seller/products/11111111-1111-1111-1111-111111111111")]
    public async Task Les_fiches_du_vendeur_exigent_un_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN JETON NE SUFFIT PLUS : LA SURFACE VENDEUR EXIGE LE RÔLE (§22).
    ///
    /// Ce groupe n'exigeait qu'un compte authentifié. N'importe quel ACHETEUR y
    /// entrait, et seule la garde d'appartenance — route par route — l'arrêtait,
    /// en rendant 404. Cela tenait tant que CHAQUE route portait sa garde,
    /// c'est-à-dire tant que personne n'en ajoutait une en l'oubliant.
    ///
    /// Le défaut serait invisible : une route vendeur ajoutée sans garde répondrait
    /// 200 à un acheteur, exactement comme elle répond 200 à son propriétaire. Rien
    /// dans la réponse ne dirait laquelle des deux protections a joué — puisque
    /// aucune n'aurait joué.
    ///
    /// 403 et non 404 : ici c'est le RÔLE qui manque, pas la ressource. Le 404 des
    /// gardes d'appartenance dit « pas à vous » sans confirmer l'existence ; le 403
    /// du groupe dit « pas cette surface », ce qui n'apprend rien à personne.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
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

    /// <summary>
    /// LECTURE PUBLIQUE N'EST PAS ÉCRITURE PUBLIQUE.
    ///
    /// Le référentiel — marques et catégories — était ouvert en écriture à tout
    /// compte inscrit. Or supprimer une catégorie emporte le rattachement de tous
    /// les produits qui la référencent, chez tous les vendeurs, d'un seul appel.
    /// </summary>
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
