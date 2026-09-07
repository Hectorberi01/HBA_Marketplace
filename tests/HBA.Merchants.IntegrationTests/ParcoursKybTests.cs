using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HBA.Shared.Hosting.Http;
using HBA.Tests.Authorization;
using Npgsql;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>LE PARCOURS KYB DU §10.3, DE BOUT EN BOUT, CONTRE UNE VRAIE BASE.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class ParcoursKybTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public ParcoursKybTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>LE TEST QUI PROUVE QUE LE SCHÉMA SE CONSTRUIT À FROID.</summary>
    [Fact]
    public async Task Le_service_demarre_et_sa_base_repond_sur_un_schema_neuf()
    {
        var reponse = await _fixture.CreateClient().GetAsync("/health/ready");

        reponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "la sonde de disponibilité interroge le DbContext : elle ne passe que si "
            + "les neuf migrations se sont appliquées dans l'ordre sur une base vide");
    }

    /// <summary>L'ANCIEN PRÉFIXE DOIT RENDRE 404 SUR LE SERVICE, ET C'EST VOULU.</summary>
    [Fact]
    public async Task L_ancien_prefixe_n_est_plus_servi_par_le_service()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create(ApiAuthorization.SellerRole));

        var reponse = await client.GetAsync("/api/merchants/me");

        reponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            .Should().Contain(chemin => chemin.StartsWith("/api/v1/merchants", StringComparison.Ordinal),
                "le document doit décrire les routes réelles du service");
    }

    /// <summary>LA SURFACE VENDEUR EXIGE LE RÔLE — VÉRIFIÉ ICI CONTRE UN HÔTE COMPLET.</summary>
    [Fact]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur()
    {
        var client = _fixture.CreateClientWithToken(TestTokens.Create());

        var reponse = await client.GetAsync($"/api/v1/merchants/{Guid.NewGuid()}");

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>L'ENVELOPPE DU §25, QUI NE S'ÉPROUVE QU'ICI.</summary>
    [Fact]
    public async Task La_fiche_vendeur_repond_dans_l_enveloppe_du_paragraphe_25()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Enveloppe {Guid.NewGuid():N}");

        var corps = await vendeur.Client.GetStringAsync($"/api/v1/merchants/{vendeur.SellerId}");

        using var document = JsonDocument.Parse(corps);
        var racine = document.RootElement;

        racine.TryGetProperty("success", out var succes).Should().BeTrue(
            "toute réponse du service doit porter l'enveloppe du §25");
        succes.GetBoolean().Should().BeTrue();

        racine.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("id").GetGuid().Should().Be(vendeur.SellerId);

        racine.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("requestId", out var requestId).Should().BeTrue(
            "c'est l'identifiant que l'utilisateur cite dans un signalement");
        requestId.GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// LA FICHE VENDEUR PORTE SES BOUTIQUES (§10.3), À PLAT ET SANS RIEN DÉPLACER.
    /// </summary>
    [Fact]
    public async Task La_fiche_vendeur_porte_ses_boutiques_sans_deplacer_ses_champs()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Boutiques {Guid.NewGuid():N}");

        var premiere = await Parcours.CreerBoutiqueAsync(vendeur, "Cotonou Centre");
        var seconde = await Parcours.CreerBoutiqueAsync(vendeur, "Porto-Novo");

        var corps = await vendeur.Client.GetStringAsync($"/api/v1/merchants/{vendeur.SellerId}");

        using var document = JsonDocument.Parse(corps);
        var data = document.RootElement.GetProperty("data");

        // Ce qui n'a pas bougé : les champs du vendeur, À PLAT, à leur place.
        data.GetProperty("id").GetGuid().Should().Be(vendeur.SellerId);
        data.GetProperty("shopName").GetString().Should().NotBeNullOrWhiteSpace();
        data.TryGetProperty("kybStatus", out _).Should().BeTrue();
        data.TryGetProperty("seller", out _).Should().BeFalse(
            "les champs du vendeur ne doivent PAS être repoussés sous un objet imbriqué : "
            + "tout client existant lit `data.shopName`");

        // Ce qui s'ajoute.
        data.TryGetProperty("stores", out var boutiques).Should().BeTrue();

        boutiques.EnumerateArray().Select(b => b.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo(new[] { premiere, seconde });
    }

    /// <summary>LE PARCOURS COMPLET — INSCRIPTION, DOSSIER, VALIDATION, ACTIVATION.</summary>
    [Fact]
    public async Task Le_parcours_kyb_active_le_vendeur_et_ses_evenements_atteignent_le_courtier()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Parcours {Guid.NewGuid():N}");

        await Parcours.DeposerPieceAsync(_fixture, vendeur, "IdCard");
        await Parcours.DeposerPieceAsync(_fixture, vendeur, "BusinessRegistry");
        await Parcours.FixerReversementAsync(vendeur);

        // Le geste explicite du lot 2.
        var soumission = await vendeur.Client.PostAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/kyb/submit", content: null);
        soumission.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var administration = Parcours.Administration(_fixture);

        (await administration.PostAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/kyb/approve", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await administration.PostAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/activate", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ─── L'état persisté, lu en SQL et non par le DbContext du service ───
        var (statut, kyb, reversement) = await LireVendeurAsync(vendeur.SellerId);

        statut.Should().Be("Active");
        kyb.Should().Be("Verified");
        reversement.Should().NotBeNull("le value object doit survivre à son converter jsonb");
        reversement!.Value.GetProperty("accountNumber").GetString().Should().Be("97000000");

        // ─── Les événements, lus sur le courtier comme un tiers les lirait ───
        var attendus = new[] { "seller.registered", "seller.kyb.approved", "seller.activated" };

        var recus = await BusDeTest.AttendreAsync(
            _fixture.BootstrapServers,
            BusDeTest.SujetMerchant,
            e => e.AggregateId == vendeur.SellerId.ToString("D") && attendus.Contains(e.EventType),
            attendu: attendus.Length);

        recus.Select(e => e.EventType).Should().BeEquivalentTo(attendus,
            "le parcours complet doit être annoncé : notifications prévient le vendeur, "
            + "identity lui greffe son rôle, et catalog n'ouvre sa boutique qu'à l'activation");
    }

    /// <summary>
    /// Lit le vendeur en SQL brut : statut, statut KYB, et le jsonb du reversement.
    /// </summary>
    private async Task<(string Statut, string Kyb, JsonElement? Reversement)> LireVendeurAsync(Guid sellerId)
    {
        await using var connexion = new NpgsqlConnection(_fixture.ConnectionString);
        await connexion.OpenAsync();

        await using var commande = new NpgsqlCommand(
            """
            SELECT "Status", "KybStatus", payout_account::text
            FROM sellers.sellers
            WHERE "Id" = @id
            """,
            connexion);

        commande.Parameters.AddWithValue("id", sellerId);

        await using var lecteur = await commande.ExecuteReaderAsync();

        (await lecteur.ReadAsync()).Should().BeTrue($"le vendeur {sellerId} doit être en base");

        var statut = lecteur.GetString(0);
        var kyb = lecteur.GetString(1);

        JsonElement? reversement = lecteur.IsDBNull(2)
            ? null
            : JsonDocument.Parse(lecteur.GetString(2)).RootElement.Clone();

        return (statut, kyb, reversement);
    }
}
