using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CHAQUE SURFACE DOIT PORTER — ET CE QU'ELLE NE DOIT PAS.
///
/// Deux changements se gardent ici, et ils tirent en sens inverse.
///
/// LA SÉPARATION DES CONTRATS (D24) A RETIRÉ SIX CHAMPS DE `SellerSummary`.
///
/// Ils ne traversaient pas le proto ; le mappeur gRPC leur donnait une valeur
/// neutre indiscernable d'une vraie, et `Payout: null` avait ainsi bloqué tous les
/// retraits de la plateforme. Ils vivent désormais sur `SellerDetail`.
///
/// Le piège : `GET /merchants/me` rendait le contrat inter-services. S'il l'avait
/// suivi dans son allègement, l'écran d'accueil de l'application vendeur aurait
/// perdu ses six champs SANS UNE ERREUR — `SellerAccount`, côté passerelle, est un
/// record positionnel sans `[JsonPropertyName]` : les champs manquants seraient
/// devenus `0` et `null` à la désérialisation. Rien n'aurait échoué.
///
/// LA FILE D'ADMINISTRATION (§6), ELLE, A DÛ EN PERDRE.
///
/// Elle rendait le résumé COMPLET de chaque vendeur — numéro Mobile Money, RCCM,
/// IFU, téléphone du gérant, références des pièces d'identité — sans pagination ni
/// filtre. Ce test-ci vérifie qu'elle ne les porte plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class FicheEtFileTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public FicheEtFileTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>
    /// LE TEST QUI EMPÊCHE `/me` DE MAIGRIR EN SILENCE.
    ///
    /// Il énumère les six champs un par un plutôt que d'en vérifier un seul :
    /// c'est une liste de compatibilité, et elle doit échouer sur celui qui
    /// disparaîtrait, pas sur un représentant.
    /// </summary>
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

    /// <summary>
    /// LA FILE NE DOIT PLUS PORTER LE FICHIER FOURNISSEURS.
    ///
    /// Le rôle administrateur reste nécessaire ; il n'est plus l'excuse de la
    /// charge utile. Ces trois champs sont ceux dont la divulgation coûtait le plus
    /// cher : la destination des virements, les papiers d'identité, les
    /// informations légales.
    /// </summary>
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

    /// <summary>
    /// La pagination du §25 : les compteurs vivent dans `meta`, pas dans `data`.
    /// </summary>
    [Fact]
    public async Task La_file_est_paginee_et_ses_compteurs_vivent_dans_meta()
    {
        // DEUX, ET NON « CEUX QUE LES AUTRES TESTS ONT LAISSÉS ».
        //
        // Les tests d'une collection partagent la base mais pas leur ordre :
        // s'appuyer sur les vendeurs d'un voisin rendrait celui-ci vert ou rouge
        // selon la place que xUnit lui donne. Il crée donc lui-même de quoi
        // dépasser une page.
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

    /// <summary>
    /// LES FACETTES SE COMPTENT SUR LA RECHERCHE, PAS SUR LA PAGE.
    ///
    /// Compter la page rendrait « 1 en revue » sur une file qui en contient
    /// quarante, et la console afficherait à son modérateur un travail dix fois
    /// plus petit que le vrai.
    /// </summary>
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

    /// <summary>
    /// Le filtre du modérateur — le seul qui manquait vraiment.
    /// </summary>
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

    /// <summary>
    /// UN FILTRE ILLISIBLE EST IGNORÉ, PAS REFUSÉ.
    ///
    /// La console construit ces valeurs depuis ses propres listes déroulantes : un
    /// 400 sur une faute de frappe transformerait une colonne mal nommée en écran
    /// blanc, et le modérateur croirait la file en panne.
    /// </summary>
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
