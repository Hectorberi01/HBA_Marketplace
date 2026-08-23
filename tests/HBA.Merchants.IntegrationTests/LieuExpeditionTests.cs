using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE LIEU D'OÙ PARTENT LES COLIS — À QUI EST-IL ?
///
/// LE JUMEAU EXACT DE LA PIÈCE KYB, ET LA MÊME DÉLÉGATION DANS LE VIDE.
///
/// `AttachStoreLocationCommand` portait : « L'appartenance du lieu au vendeur
/// n'est pas vérifiée ici. […] Le contrôle est fait par l'appelant, qui voit les
/// deux modules — voir la route du BFF Vendeur. » Le BFF Vendeur annonce lui-même
/// n'exposer aucun cas d'usage.
///
/// N'importe quel GUID passait. `Store.Open()` acceptait ensuite la boutique, et
/// l'identifiant partait vers delivery, qui bâtissait un enlèvement coursier sur
/// une adresse que le vendeur ne contrôle pas.
///
/// ET LE GUID INEXISTANT ÉTAIT LE PIRE DES TROIS.
///
/// Il ne se manifestait qu'APRÈS le paiement de l'acheteur, sur la jambe coursier
/// — au moment le plus cher du parcours, et le plus difficile à rattraper.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
[Collection(MerchantsIntegrationCollection.Nom)]
// SANS CE TRAIT, LA CLASSE TOURNE DANS `make test` ET ÉCHOUE SUR UN POSTE
// SANS DOCKER. C'est le filtre de la cible `test` — voir le Makefile.
[Trait("Docker", "true")]
public sealed class LieuExpeditionTests
{
    private readonly MerchantsIntegrationFixture _fixture;

    public LieuExpeditionTests(MerchantsIntegrationFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Son_propre_lieu_est_rattache()
    {
        var vendeur = await ActiverAsync($"Lieu {Guid.NewGuid():N}");
        var boutique = await Parcours.CreerBoutiqueAsync(vendeur, "Akpakpa");
        var lieu = _fixture.Inventaire.Deposer(vendeur.SellerId);

        var reponse = await RattacherAsync(vendeur, boutique, lieu);

        reponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// LE TEST QUI EMPÊCHE D'EXPÉDIER DEPUIS CHEZ UN CONCURRENT.
    /// </summary>
    [Fact]
    public async Task Le_lieu_d_un_autre_vendeur_est_refuse()
    {
        var victime = await ActiverAsync($"Victime lieu {Guid.NewGuid():N}");
        var attaquant = await ActiverAsync($"Attaquant lieu {Guid.NewGuid():N}");

        var boutique = await Parcours.CreerBoutiqueAsync(attaquant, "Godomey");
        var lieuDeLaVictime = _fixture.Inventaire.Deposer(victime.SellerId);

        var reponse = await RattacherAsync(attaquant, boutique, lieuDeLaVictime);

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "un identifiant de lieu ne prouve rien : le colis partirait d'une adresse "
            + "que ce vendeur ne contrôle pas");

        (await LireRaisonAsync(reponse)).Should().Be("sellers.store.location_not_owned");
    }

    /// <summary>
    /// CELUI QUI COÛTAIT LE PLUS CHER : UN GUID QUI NE DÉSIGNE RIEN.
    ///
    /// La boutique s'ouvrait, l'offre partait en vente, l'acheteur payait — et
    /// c'est le coursier qui découvrait qu'il n'y avait pas d'adresse.
    /// </summary>
    [Fact]
    public async Task Un_lieu_inexistant_est_refuse()
    {
        var vendeur = await ActiverAsync($"Lieu fantome {Guid.NewGuid():N}");
        var boutique = await Parcours.CreerBoutiqueAsync(vendeur, "Fidjrossè");

        var reponse = await RattacherAsync(vendeur, boutique, Guid.NewGuid());

        reponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.store.location_not_found");
    }

    /// <summary>
    /// UN ENTREPÔT PLATEFORME EST REFUSÉ, ET C'EST UN CHOIX.
    ///
    /// Son `OwnerId` est nul par construction (FBP). L'accepter rendrait la garde
    /// inopérante — n'importe quel vendeur pointerait n'importe quel entrepôt.
    /// Confier une boutique à un entrepôt de la plateforme est une décision
    /// d'exploitation ; le jour où elle sera nécessaire, elle méritera sa propre
    /// route d'administration, nommée pour ce qu'elle fait.
    /// </summary>
    [Fact]
    public async Task Un_entrepot_plateforme_n_est_pas_rattachable_par_le_vendeur()
    {
        var vendeur = await ActiverAsync($"Entrepot {Guid.NewGuid():N}");
        var boutique = await Parcours.CreerBoutiqueAsync(vendeur, "Cadjèhoun");

        var entrepot = _fixture.Inventaire.Deposer(ownerId: null, type: "PlatformWarehouse");

        var reponse = await RattacherAsync(vendeur, boutique, entrepot);

        reponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LireRaisonAsync(reponse)).Should().Be("sellers.store.location_not_owned");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Outillage
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Un vendeur ACTIF : `CreateStoreCommand` refuse une boutique à qui ne l'est
    /// pas — « seul un vendeur actif peut ouvrir une boutique ».
    /// </summary>
    private async Task<VendeurInscrit> ActiverAsync(string nomBoutique)
    {
        var vendeur = await Parcours.InscrireAsync(_fixture, nomBoutique);

        await Parcours.DeposerPieceAsync(_fixture, vendeur);
        await Parcours.FixerReversementAsync(vendeur);

        var administration = Parcours.Administration(_fixture);

        (await administration.PostAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/kyb/approve", content: null))
            .EnsureSuccessStatusCode();

        (await administration.PostAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/activate", content: null))
            .EnsureSuccessStatusCode();

        return vendeur;
    }

    private static Task<HttpResponseMessage> RattacherAsync(
        VendeurInscrit vendeur, Guid storeId, Guid locationId)
        => vendeur.Client.PutAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/stores/{storeId}/location",
            new { fulfillmentLocationId = locationId });

    /// <summary>Le code fin du domaine vit dans `error.details[field=reason]` — voir `PieceKybTests`.</summary>
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
}
