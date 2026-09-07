using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>À QUI APPARTIENT LA PIÈCE QU'ON RATTACHE — LE CONTRÔLE QUI N'EXISTAIT PAS.</summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE SANS
// DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class PieceKybTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public PieceKybTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    /// <summary>Le cas nominal : son propre fichier, de la bonne nature, prêt.</summary>
    [Fact]
    public async Task Sa_propre_piece_est_rattachee()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Piece {Guid.NewGuid():N}");
        var mediaId = _fixture.Media.Deposer(
            vendeur.SellerId, deposeParUserId: vendeur.UserId);

        var reponse = await Parcours.RattacherPieceAsync(vendeur, mediaId);

        reponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>LE TEST QUI FERME LA FUITE DE PIÈCES D'IDENTITÉ.</summary>
    [Fact]
    public async Task La_piece_d_un_autre_vendeur_est_refusee()
    {
        var victime = await Parcours.InscrireAsync(_fixture, $"Victime {Guid.NewGuid():N}");
        var attaquant = await Parcours.InscrireAsync(_fixture, $"Attaquant {Guid.NewGuid():N}");

        var pieceDeLaVictime = _fixture.Media.Deposer(victime.SellerId);

        var reponse = await Parcours.RattacherPieceAsync(attaquant, pieceDeLaVictime);

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "connaître un identifiant de média ne doit pas suffire — ils circulent, "
            + "la vitrine en rend un par image de fiche produit");

        (await LireRaisonAsync(reponse)).Should().Be("sellers.kyb.media_not_owned");

        // ET LE DOSSIER NE DOIT PAS AVOIR BOUGÉ.
        var fiche = await LireFicheAsync(attaquant);
        fiche.GetProperty("kybStatus").GetString().Should().Be("NotStarted");
        fiche.GetProperty("kybDocuments").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Un média qui n'existe pas — le cas qui passait le plus facilement, puisqu'il
    /// suffisait d'un `Guid.NewGuid()`.
    /// </summary>
    [Fact]
    public async Task Un_media_inexistant_est_refuse()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Fantome {Guid.NewGuid():N}");

        var reponse = await Parcours.RattacherPieceAsync(vendeur, Guid.NewGuid());

        reponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.kyb.media_not_found");
    }

    /// <summary>SON PROPRE FICHIER, MAIS PAS UNE PIÈCE LÉGALE.</summary>
    [Fact]
    public async Task Une_photo_de_boutique_n_est_pas_une_piece_legale()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Vitrine {Guid.NewGuid():N}");
        var photo = _fixture.Media.Deposer(
            vendeur.SellerId, mediaType: "StoreMedia", deposeParUserId: vendeur.UserId);

        var reponse = await Parcours.RattacherPieceAsync(vendeur, photo);

        reponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.kyb.media_wrong_kind");
    }

    /// <summary>PAS ENCORE PRÊT N'EST PAS ABSENT, ET LE MESSAGE DOIT LE DIRE.</summary>
    [Fact]
    public async Task Un_fichier_encore_en_traitement_est_refuse_sans_ambiguite()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Traitement {Guid.NewGuid():N}");
        var enCours = _fixture.Media.Deposer(
            vendeur.SellerId, status: "Processing", deposeParUserId: vendeur.UserId);

        var reponse = await Parcours.RattacherPieceAsync(vendeur, enCours);

        reponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.kyb.media_not_ready");
    }

    /// <summary>LE BON IDENTIFIANT, LE MAUVAIS TYPE DE PROPRIÉTAIRE.</summary>
    [Fact]
    public async Task Un_media_du_bon_identifiant_mais_du_mauvais_proprietaire_est_refuse()
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, $"Homonyme {Guid.NewGuid():N}");
        var media = _fixture.Media.Deposer(
            vendeur.SellerId, ownerType: "Store", deposeParUserId: vendeur.UserId);

        var reponse = await Parcours.RattacherPieceAsync(vendeur, media);

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.kyb.media_not_owned");
    }

    // Outillage

    /// <summary>Lit le code FIN du domaine, qui vit dans `error.details[field=reason]`.</summary>
    private static async Task<string?> LireRaisonAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        if (!corps.TryGetProperty("error", out var erreur)
            || !erreur.TryGetProperty("details", out var details))
        {
            return null;
        }

        return details.EnumerateArray()
            .Where(d => d.GetProperty("field").GetString() == "reason")
            .Select(d => d.GetProperty("message").GetString())
            .FirstOrDefault();
    }

    private static async Task<JsonElement> LireFicheAsync(VendeurInscrit vendeur)
    {
        var corps = await vendeur.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/merchants/{vendeur.SellerId}");

        return corps.GetProperty("data");
    }
}
