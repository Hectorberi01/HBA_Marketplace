using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>CE QUE CHAQUE SURFACE DOIT PORTER — ET CE QU'ELLE NE DOIT PAS.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class FicheEtFileTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public FicheEtFileTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>LE TEST QUI EMPÊCHE `/me` DE MAIGRIR EN SILENCE.</summary>
    [Fact]
    public async Task La_fiche_me_porte_tout_ce_que_l_application_vendeur_lit()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Me {Guid.NewGuid():N}");
        await Parcours.FixerReversementAsync(vendeur);
        await Parcours.DeposerPieceAsync(_fixture, vendeur);
        await Parcours.CreerBoutiqueAsync(vendeur, "Ganhi");

        var corps = await vendeur.Client.GetFromJsonAsync<JsonElement>("/api/v1/merchants/me");
        var data = corps.GetProperty("data");

        // Les huit champs transportés, qui n'ont pas bougé.
        data.GetProperty("id").GetGuid().Should().Be(vendeur.SellerId);
        data.GetProperty("shopName").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("commissionRate").GetDecimal().Should().BeGreaterThan(0m);

        // Les six qui ont quitté `SellerSummary` et DOIVENT rester servis ici.
        foreach (var champ in new[]
                 {
                     "rating", "salesCount", "payout", "kybDocuments",
                     "metadata", "kybRejectionReason"
                 })
        {
            data.TryGetProperty(champ, out _).Should().BeTrue(
                $"« {champ} » a quitté le contrat inter-services, pas la fiche du vendeur");
        }

        data.GetProperty("payout").GetProperty("accountNumber").GetString().Should().Be("97000000");
        data.GetProperty("kybDocuments").GetArrayLength().Should().Be(1);

        // Et les boutiques, comme sur `GET /merchants/{id}` : les deux chemins
        // servent le même écran.
        data.GetProperty("stores").GetArrayLength().Should().Be(1);
    }

    /// <summary>LA FILE NE DOIT PLUS PORTER LE FICHIER FOURNISSEURS.</summary>
    [Fact]
    public async Task La_file_d_administration_ne_porte_ni_compte_de_retrait_ni_pieces()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"File {Guid.NewGuid():N}");
        await Parcours.FixerReversementAsync(vendeur);
        await Parcours.DeposerPieceAsync(_fixture, vendeur);

        var corps = await Parcours.Administration(_fixture)
            .GetFromJsonAsync<JsonElement>("/api/v1/merchants/?pageSize=100");

        var ligne = corps.GetProperty("data").EnumerateArray()
            .Single(v => v.GetProperty("id").GetGuid() == vendeur.SellerId);

        foreach (var interdit in new[] { "payout", "kybDocuments", "metadata", "commissionRate" })
        {
            ligne.TryGetProperty(interdit, out _).Should().BeFalse(
                $"« {interdit} » n'a rien à faire dans une file de modération : "
                + "il est à un clic, sur la fiche que l'administrateur ouvre");
        }

        // Ce qu'un modérateur cherche, en revanche, doit y être.
        ligne.GetProperty("kybStatus").GetString().Should().Be("InReview");
        ligne.GetProperty("kybDocumentCount").GetInt32().Should().Be(1);
    }

    /// <summary>La pagination du §25 : les compteurs vivent dans `meta`, pas dans `data`.</summary>
    [Fact]
    public async Task La_file_est_paginee_et_ses_compteurs_vivent_dans_meta()
    {
        // DEUX, ET NON « CEUX QUE LES AUTRES TESTS ONT LAISSÉS ».
        await Parcours.InscrireAsync(_fixture, $"Page A {Guid.NewGuid():N}");
        await Parcours.InscrireAsync(_fixture, $"Page B {Guid.NewGuid():N}");

        var corps = await Parcours.Administration(_fixture)
            .GetFromJsonAsync<JsonElement>("/api/v1/merchants/?page=1&pageSize=1");

        corps.GetProperty("data").GetArrayLength().Should().Be(1,
            "`pageSize=1` doit rendre une ligne, pas la table entière");

        var meta = corps.GetProperty("meta");
        meta.GetProperty("page").GetInt32().Should().Be(1);
        meta.GetProperty("pageSize").GetInt32().Should().Be(1);
        meta.GetProperty("total").GetInt64().Should().BeGreaterThan(1,
            "le total compte la file, pas la page");
        meta.GetProperty("hasNext").GetBoolean().Should().BeTrue();
    }

    /// <summary>LES FACETTES SE COMPTENT SUR LA RECHERCHE, PAS SUR LA PAGE.</summary>
    [Fact]
    public async Task Les_facettes_comptent_la_file_entiere_et_non_la_page()
    {
        var marqueur = $"Facette{Guid.NewGuid():N}";

        var premier = await Parcours.InscrireAsync(_fixture, $"{marqueur} A");
        await Parcours.DeposerPieceAsync(_fixture, premier);

        var second = await Parcours.InscrireAsync(_fixture, $"{marqueur} B");
        await Parcours.DeposerPieceAsync(_fixture, second);

        // Une seule ligne par page, mais la recherche en couvre deux.
        var corps = await Parcours.Administration(_fixture)
            .GetFromJsonAsync<JsonElement>($"/api/v1/merchants/?page=1&pageSize=1&search={marqueur}");

        corps.GetProperty("data").GetArrayLength().Should().Be(1);

        var facettes = corps.GetProperty("meta").GetProperty("facets");
        facettes.GetProperty("InReview").GetInt32().Should().Be(2,
            "les deux dossiers de la recherche sont en revue, même si la page n'en montre qu'un");
    }

    /// <summary>Le filtre du modérateur — le seul qui manquait vraiment.</summary>
    [Fact]
    public async Task Le_filtre_sur_le_statut_kyb_ecarte_les_dossiers_non_commences()
    {
        var marqueur = $"Filtre{Guid.NewGuid():N}";

        var sansDossier = await Parcours.InscrireAsync(_fixture, $"{marqueur} A");
        var enRevue = await Parcours.InscrireAsync(_fixture, $"{marqueur} B");
        await Parcours.DeposerPieceAsync(_fixture, enRevue);

        var corps = await Parcours.Administration(_fixture)
            .GetFromJsonAsync<JsonElement>(
                $"/api/v1/merchants/?search={marqueur}&kybStatus=InReview&pageSize=100");

        var ids = corps.GetProperty("data").EnumerateArray()
            .Select(v => v.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(enRevue.SellerId);
        ids.Should().NotContain(sansDossier.SellerId);
    }

    /// <summary>UN FILTRE ILLISIBLE EST IGNORÉ, PAS REFUSÉ.</summary>
    [Fact]
    public async Task Un_statut_kyb_inconnu_ne_filtre_rien_et_ne_casse_pas_l_ecran()
    {
        var marqueur = $"Inconnu{Guid.NewGuid():N}";
        var vendeur = await Parcours.InscrireAsync(_fixture, $"{marqueur} A");

        var reponse = await Parcours.Administration(_fixture)
            .GetAsync($"/api/v1/merchants/?search={marqueur}&kybStatus=Fromage");

        reponse.IsSuccessStatusCode.Should().BeTrue();

        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        corps.GetProperty("data").EnumerateArray()
            .Select(v => v.GetProperty("id").GetGuid())
            .Should().Contain(vendeur.SellerId);
    }
}
