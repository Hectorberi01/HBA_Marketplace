using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// À QUI APPARTIENT LA PIÈCE QU'ON RATTACHE — LE CONTRÔLE QUI N'EXISTAIT PAS.
///
/// CE SERVICE ACCEPTAIT N'IMPORTE QUEL GUID COMME PIÈCE D'IDENTITÉ.
///
/// `Seller.AddKybDocument` refuse un `mediaId` vide, et rien d'autre. Son encadré
/// délègue le reste « à l'appelant — la couche qui voit les deux ». Cette couche,
/// c'était le handler ; il ne le faisait pas. La documentation renvoyait ensuite
/// au BFF Vendeur, qui annonce lui-même n'exposer aucun cas d'usage. La
/// délégation ne pointait vers personne.
///
/// DEUX EXPLOITATIONS, À LA PORTÉE D'UN VENDEUR INSCRIT :
///
///   1. Rattacher le média d'un concurrent à SON dossier, puis s'en faire signer
///      l'URL — media-service ne vérifie pas le droit métier, sa route le dit.
///      C'est mot pour mot la faille que le passage de `FileUrl` à `MediaId`
///      devait fermer.
///
///   2. Rattacher puis RETIRER : le retrait fait supprimer le fichier chez
///      media-service. Une primitive de suppression arbitraire contre les photos
///      produit, les visuels de restaurant, ou le dossier KYB d'autrui.
///
/// CES TESTS N'AURAIENT PAS PU EXISTER AVANT LE FAUX PILOTABLE.
///
/// `MediaDeTest` laisse chaque test choisir le propriétaire, la nature et l'état
/// du fichier. Un faux qui dirait toujours oui — comme `IdentiteDeTest` — rendrait
/// vert un service ayant reperdu son contrôle.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
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

    /// <summary>
    /// LE TEST QUI FERME LA FUITE DE PIÈCES D'IDENTITÉ.
    ///
    /// Deux vendeurs, un fichier appartenant au premier, rattaché par le second.
    /// C'est exactement le geste qui donnait accès aux papiers d'un concurrent :
    /// une fois la pièce sur son dossier, elle ressort dans SA fiche, et l'URL
    /// signée se demande nommément.
    /// </summary>
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

        // ET LE DOSSIER NE DOIT PAS AVOIR BOUGÉ. Un refus qui laisserait le
        // dossier en revue mettrait l'administrateur devant une pièce qui n'est
        // pas là — et la bascule dépréciée se déclenche AU RATTACHEMENT.
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

    /// <summary>
    /// SON PROPRE FICHIER, MAIS PAS UNE PIÈCE LÉGALE.
    ///
    /// Un vendeur possède aussi ses images de boutique — publiques, servies par le
    /// CDN. Sans ce contrôle, il présentait une photo de devanture comme sa carte
    /// d'identité : le dossier partait en validation, et l'administrateur
    /// découvrait la pièce manquante en l'ouvrant.
    /// </summary>
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

    /// <summary>
    /// PAS ENCORE PRÊT N'EST PAS ABSENT, ET LE MESSAGE DOIT LE DIRE.
    ///
    /// Le traitement du fichier est asynchrone. Rattacher avant sa fin mettrait
    /// dans la file de validation un dossier dont l'administrateur ne pourrait pas
    /// ouvrir la pièce — il ne pourrait que le refuser, et le vendeur ne saurait
    /// pas pourquoi.
    /// </summary>
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

    /// <summary>
    /// LE BON IDENTIFIANT, LE MAUVAIS TYPE DE PROPRIÉTAIRE.
    ///
    /// Le couple `(OwnerType, OwnerId)` est la seule chose que media-service
    /// connaisse d'un rattachement. Comparer `OwnerId` seul laisserait passer un
    /// média dont l'identifiant de propriétaire coïncide par hasard avec un
    /// sellerId — et rien n'empêche un `Store` ou un `User` de porter le même GUID
    /// dans un dépôt où les identifiants sont tirés indépendamment.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lit le code FIN du domaine, qui vit dans `error.details[field=reason]`.
    ///
    /// PAS `error.code`, ET C'EST UNE PROPRIÉTÉ DU SOCLE, PAS UN DÉTOUR.
    ///
    /// `ApiResults` normalise `error.code` sur les cinq codes du cahier —
    /// `VALIDATION_ERROR`, `FORBIDDEN`… — pour que le client sache COMMENT réagir.
    /// Le code fin, qui dit CE QUI s'est passé, part dans `details` sous `reason`.
    /// Assérer sur le code normalisé rendrait ces tests incapables de distinguer
    /// « pas à vous » de « mauvaise nature », qui sont pourtant deux règles
    /// différentes.
    /// </summary>
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
