using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HBA.Tests.Authorization;
using Xunit;

namespace HBA.Catalog.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PARCOURS DU §28, DE BOUT EN BOUT, CONTRE UNE VRAIE BASE.
///
/// CE QUE LES TESTS UNITAIRES NE PEUVENT PAS DIRE, ET QU'ON A DÉJÀ PAYÉ.
///
/// `ProductLifecycleTests` éprouve les règles de l'agrégat — et il les éprouve
/// bien : c'est lui qui a trouvé la transition « correction » manquante du §4.
/// Mais il travaille sur des objets en mémoire. Il ne dit rien de :
///
///   • la persistance des révisions et de leurs spécifications (lot 5) — un
///     `.Include` oublié rend une révision sans sa fiche technique, et l'agrégat
///     ne s'en plaint pas ;
///   • l'enveloppe du §25 (lot 6) — la forme réelle du corps HTTP ;
///   • le préfixe `/api/v1/` (lot 6) — une route mal montée rend 404, et aucun
///     test unitaire ne monte de routes ;
///   • les index partiels — `ux_product_revisions_published_slug` ne s'exprime
///     qu'en base.
///
/// Chacun de ces quatre points a produit un défaut réel dans les lots précédents.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(CatalogIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class ParcoursProduitTests
{
    private readonly CatalogIntegrationFixture _fixture;

    public ParcoursProduitTests(CatalogIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// LE TEST QUI PROUVE QUE LE SCHÉMA SE CONSTRUIT À FROID.
    ///
    /// Il n'assère presque rien — mais pour qu'il s'exécute, le service a dû
    /// appliquer TOUTES ses migrations sur une base vide. L'audit notait que ce
    /// départ à froid n'avait jamais été rejoué : `check-migrations.py` le simule
    /// en lisant les fichiers, il ne l'exécute pas.
    ///
    /// Une migration incohérente fait échouer ce test — et tous les autres — au
    /// démarrage de la fixture, avec l'erreur PostgreSQL exacte.
    /// </summary>
    [Fact]
    public async Task Le_service_demarre_et_sert_la_vitrine_sur_une_base_neuve()
    {
        var reponse = await _fixture.CreateClient().GetAsync("/api/v1/catalog/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// LA FORME DE L'ENVELOPPE, QUI NE S'ÉPROUVE QU'ICI.
    ///
    /// Le lot 6 a migré 59 réponses vers `ApiResults`. Rien, dans le code, ne
    /// garantit qu'aucune n'a été oubliée : un `Results.Ok` restant compile et rend
    /// une réponse d'apparence correcte, simplement pas enveloppée. Le client la
    /// lit avec son parseur d'enveloppe et obtient des champs nuls — c'est
    /// exactement le mode de panne silencieux trouvé dans `CatalogClient`.
    /// </summary>
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

    /// <summary>
    /// L'ANCIEN CHEMIN DOIT RENDRE 404 SUR LE SERVICE, ET C'EST VOULU.
    ///
    /// La coquille de dépréciation vit à la PASSERELLE, pas ici (décision D15). Ce
    /// test fixe la frontière : si quelqu'un « corrigeait » ce 404 en remontant
    /// l'ancien préfixe dans le service, on aurait deux endroits qui servent la
    /// même surface, et le retrait de la coquille ne retirerait plus rien.
    ///
    /// LA REQUÊTE EST AUTHENTIFIÉE, ET SANS CELA LE TEST NE PROUVE RIEN.
    ///
    /// `AddHbaService` pose une politique de REPLI qui exige un compte authentifié,
    /// et ASP.NET Core l'applique AUSSI aux requêtes qui ne correspondent à AUCUN
    /// point de terminaison. Un appel anonyme sur un chemin inexistant rend donc
    /// 401, jamais 404 — c'est ce qui rend l'oubli fermé par défaut, et c'est
    /// voulu.
    ///
    /// Sans jeton, ce test passait pour de mauvaises raisons : un 401 aurait aussi
    /// bien signalé « route absente » que « route présente et protégée ». Avec un
    /// jeton, le 404 dit exactement ce qu'on veut savoir — le service ne route plus
    /// ce chemin.
    /// </summary>
    [Fact]
    public async Task L_ancien_prefixe_n_est_plus_servi_par_le_service()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create());

        var reponse = await client.GetAsync("/api/catalog/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// LA SURFACE VENDEUR EXIGE LE RÔLE — VÉRIFIÉ ICI CONTRE UN HÔTE COMPLET.
    ///
    /// `CatalogPublicRoutesTests` le vérifie déjà sans base. La redite n'est pas
    /// gratuite : elle éprouve la même règle une fois que TOUT est monté — base,
    /// outbox, consommateur, télémétrie. Un intercepteur ou un filtre ajouté plus
    /// tard pourrait très bien changer l'ordre du pipeline sans que la suite sans
    /// base ne le voie.
    /// </summary>
    [Fact]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create());

        var reponse = await client.GetAsync("/api/v1/catalog/seller/products");

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// LA DOCUMENTATION DOIT ÊTRE ATTEIGNABLE SANS JETON (lot 7).
    ///
    /// C'est le piège décrit dans `UseHbaOpenApi` : placée après
    /// `UseAuthorization`, la page répondrait 401 avant d'avoir pu servir le bouton
    /// « Authorize » qui permet de s'authentifier. On tourne en rond, et rien dans
    /// le message ne l'explique. Ce test fige l'ordre du pipeline.
    /// </summary>
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
