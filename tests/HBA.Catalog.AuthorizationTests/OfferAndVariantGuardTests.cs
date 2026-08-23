using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.AuthorizationTests;

/// <summary>
/// Les routes ouvertes en phase 3 et par les tâches #179 / #230.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// VINGT-DEUX ROUTES ONT ÉTÉ OUVERTES AU VENDEUR SANS AUCUN TEST DE FRONTIÈRE.
///
/// Neuf routes d'offres (phase 3), douze routes produit qu'on vient de refermer
/// (#179), une bascule de déclinaison (#230). Chacune porte une garde
/// d'appartenance écrite à la main. Rien ne vérifiait qu'elle tenait.
///
/// Ce n'est pas une hypothèse : la même session a trouvé trois routes financières
/// dont la garde était ANNONCÉE dans un commentaire et absente du code. Une garde
/// non testée est une garde dont on croit qu'elle existe.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CES TESTS ÉPROUVENT, ET CE QU'ILS N'ÉPROUVENT PAS.
///
/// La fabrique ne monte pas de base de données : une requête qui franchit
/// l'autorisation échoue ensuite dans le gestionnaire. On ne peut donc PAS
/// distinguer « la garde a refusé » de « la garde a laissé passer, puis la base
/// manquait ».
///
/// D'où la forme des assertions : elles portent sur ce qui se décide AVANT le
/// gestionnaire — 401 sans jeton, 403 sans rôle. C'est la couche que le
/// `FallbackPolicy` et les groupes gouvernent, et c'est celle qui a lâché dans
/// tous les incidents de cette session.
///
/// CE QUI RESTE À COUVRIR, ET QUI N'EST PAS ICI : le refus 404 sur la fiche
/// d'AUTRUI. Il demande une base peuplée avec deux vendeurs — donc un test
/// d'intégration, dans tests/integration. Le noter est plus honnête que de
/// prétendre l'avoir fait avec un test qui ne monte rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class OfferAndVariantGuardTests : IClassFixture<AuthorizationTestFactory<Program>>
{
    private readonly AuthorizationTestFactory<Program> _factory;

    public OfferAndVariantGuardTests(AuthorizationTestFactory<Program> factory) => _factory = factory;

    private static string Route(string gabarit) => gabarit
        .Replace("{id}", Guid.NewGuid().ToString())
        .Replace("{variantId}", Guid.NewGuid().ToString())
        .Replace("{storeId}", Guid.NewGuid().ToString())
        .Replace("{mediaId}", Guid.NewGuid().ToString());

    /// <summary>
    /// Les vingt-deux routes vendeur refusent un appel SANS JETON.
    /// </summary>
    /// <remarks>
    /// C'EST LE GROUPE QUI GARANTIT CELA, PAS CHAQUE ROUTE — et c'est
    /// précisément pourquoi il faut le tester. `MapSellerGroup` porte
    /// l'exigence une seule fois ; une route déclarée par erreur sur
    /// `publicCatalog` la perdrait sans qu'aucun compilateur ne s'en aperçoive.
    /// C'est arrivé : trois écritures de règlement vivaient dans le groupe
    /// authentifié au lieu du groupe admin.
    /// </remarks>
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

    /// <summary>
    /// La vitrine PUBLIQUE ne doit pas rendre les routes vendeur atteignables.
    /// </summary>
    /// <remarks>
    /// CE TEST GARDE UN PIÈGE D'ORDONNANCEMENT, PAS UNE POLITIQUE.
    ///
    /// `publicCatalog` est déclaré sur `/api/v1/catalog` et `AllowAnonymous` sur ses
    /// routes. Le groupe vendeur est sur `/api/v1/catalog/seller`. Rien n'empêche
    /// quelqu'un d'ajouter demain, sur le groupe public, un
    /// `MapGet("/seller/...")` par commodité — et de rendre anonyme une lecture de
    /// catalogue vendeur.
    ///
    /// La lecture des offres d'une boutique est la plus exposée : elle porte le
    /// prix NET du vendeur, sa commission et sa marge.
    /// </remarks>
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
    /// ═════════════════════════════════════════════════════════════════════════
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
    /// ═════════════════════════════════════════════════════════════════════════
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

    /// <summary>
    /// ET LE PENDANT : UN ACHETEUR N'ENTRE PLUS DU TOUT.
    ///
    /// C'est ce que le rôle apporte, et cela n'était vérifié nulle part avant le
    /// lot 6 — la seule barrière était la garde d'appartenance de chaque route,
    /// donc une discipline, pas une politique.
    /// </summary>
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

    /// <summary>
    /// L'administration du référentiel reste fermée à un compte sans rôle.
    /// </summary>
    /// <remarks>
    /// REDITE VOLONTAIRE avec `CatalogPublicRoutesTests`, sur les DEUX routes
    /// que la phase 3 a fait voisiner avec les offres. Le segment `/admin` n'a
    /// jamais été une politique — il a fallu un `MapAdminGroup` pour cela — et une
    /// route d'offre déposée par erreur dans ce groupe deviendrait réservée aux
    /// modérateurs, ce qui casserait l'espace vendeur sans rien fuir. Le test dit
    /// donc aussi où les offres NE sont pas.
    /// </remarks>
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
