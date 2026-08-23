using System.Net.Http.Json;
using System.Text.Json;
using HBA.Shared.Hosting.Http;
using HBA.Tests.Authorization;

namespace HBA.Merchants.IntegrationTests;

/// <summary>Un vendeur inscrit par le parcours réel, avec de quoi continuer à agir en son nom.</summary>
internal sealed record VendeurInscrit(Guid UserId, Guid SellerId, HttpClient Client);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES GESTES DU PARCOURS, PASSÉS PAR LA VRAIE SURFACE HTTP.
///
/// AUCUN RACCOURCI PAR LE `DbContext` OU PAR L'AGRÉGAT.
///
/// Il serait plus rapide d'insérer un vendeur en base et de partir de là. Ce
/// serait aussi renoncer à la moitié de ce que ce niveau existe pour éprouver :
/// le préfixe `/api/v1/`, l'enveloppe du §25, les gardes de propriété, la
/// sérialisation des value objects en jsonb, et le fait que chaque écriture
/// alimente réellement l'outbox. Un vendeur posé directement en base n'aurait
/// jamais rien publié.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class Parcours
{
    /// <summary>
    /// Inscrit un vendeur et rend de quoi agir en son nom.
    /// </summary>
    /// <remarks>
    /// LE JETON PORTE DÉJÀ LE RÔLE `Seller`, ET C'EST UN RACCOURCI ASSUMÉ.
    ///
    /// En vrai, le rôle est greffé APRÈS l'inscription :
    /// `SellerRegisteredIntegrationEvent` part sur le courtier, identity-service le
    /// consomme et l'ajoute au compte — donc le vendeur ne l'obtient qu'à son jeton
    /// suivant. Le reproduire ici demanderait de faire tourner identity-service.
    ///
    /// Ce que cela ne masque pas : `MerchantsAuthorizationTests` vérifie que
    /// l'inscription et `GET /me` restent ouvertes à un compte SANS le rôle — c'est
    /// exactement l'exception qui rend le parcours possible, et elle est tenue
    /// ailleurs.
    /// </remarks>
    public static async Task<VendeurInscrit> InscrireAsync(
        MerchantsIntegrationFixture fixture, string nomBoutique)
    {
        var userId = Guid.NewGuid();
        var client = fixture.CreateClientWithToken(
            TestTokens.Create(userId, ApiAuthorization.SellerRole));

        var reponse = await client.PostAsJsonAsync(
            "/api/v1/merchants",
            new { shopName = nomBoutique, commissionRate = 0.10m });

        reponse.EnsureSuccessStatusCode();

        return new VendeurInscrit(userId, await LireIdAsync(reponse), client);
    }

    /// <summary>
    /// Téléverse un fichier au nom du vendeur, puis le rattache à son dossier KYB.
    /// Bascule le dossier en revue (comportement déprécié).
    /// </summary>
    /// <remarks>
    /// LE MÉDIA EST DÉPOSÉ AVANT, ET IL N'EST PLUS UN GUID AU HASARD.
    ///
    /// Cette méthode envoyait `Guid.NewGuid()`. Elle passait, parce que le service
    /// ne vérifiait rien : n'importe quel identifiant devenait une pièce
    /// d'identité. Le contrôle de propriété ajouté au §2 de l'audit rend ce
    /// raccourci impossible, et c'est le signe qu'il fonctionne — un test qui
    /// aurait continué de passer aurait signalé un contrôle inopérant.
    /// </remarks>
    public static async Task<Guid> DeposerPieceAsync(
        MerchantsIntegrationFixture fixture, VendeurInscrit vendeur, string type = "IdCard")
    {
        var mediaId = fixture.Media.Deposer(vendeur.SellerId);

        var reponse = await RattacherPieceAsync(vendeur, mediaId, type);

        reponse.EnsureSuccessStatusCode();

        return await LireIdAsync(reponse);
    }

    /// <summary>
    /// Rattache un média BRUT, sans exiger le succès — pour éprouver les refus.
    /// </summary>
    public static Task<HttpResponseMessage> RattacherPieceAsync(
        VendeurInscrit vendeur, Guid mediaId, string type = "IdCard")
        => vendeur.Client.PostAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/kyb/documents",
            new { type, mediaId });

    /// <summary>Fixe les coordonnées de reversement — exigées par l'activation.</summary>
    public static async Task FixerReversementAsync(VendeurInscrit vendeur)
    {
        var reponse = await vendeur.Client.PutAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/payout-account",
            new { provider = "MtnMomo", accountNumber = "97000000", accountName = "Kossi Adjovi" });

        reponse.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Le MÊME vendeur, mais avec un jeton dont l'authentification a vieilli.
    /// </summary>
    /// <remarks>
    /// SERT À PROUVER QUE LE STEP-UP DU §37 MORD ENCORE.
    ///
    /// `TestTokens.Create` pose un `auth_time` frais, sans quoi tout le parcours se
    /// verrait refuser `PUT /payout-account` en 403. Le risque de ce claim est
    /// qu'en le posant partout, plus aucun test ne prouve que le contrôle existe :
    /// on aurait désactivé une garde de sécurité pour faire passer une suite.
    ///
    /// Ce client-ci est la contre-épreuve. Même compte, mêmes rôles, même
    /// appartenance — seule l'ancienneté de l'authentification change.
    /// </remarks>
    public static VendeurInscrit AvecAuthentificationAncienne(
        MerchantsIntegrationFixture fixture, VendeurInscrit vendeur)
        => vendeur with
        {
            Client = fixture.CreateClientWithToken(
                TestTokens.CreateAuthentificationAncienne(vendeur.UserId, ApiAuthorization.SellerRole))
        };

    /// <summary>Le vendeur corrige son numéro de reversement.</summary>
    public static async Task CorrigerReversementAsync(VendeurInscrit vendeur, string numero)
    {
        var reponse = await vendeur.Client.PutAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/payout-account",
            new { provider = "MtnMomo", accountNumber = numero, accountName = "Kossi Adjovi" });

        reponse.EnsureSuccessStatusCode();
    }

    /// <summary>Crée une boutique pour ce vendeur.</summary>
    public static async Task<Guid> CreerBoutiqueAsync(VendeurInscrit vendeur, string nom)
    {
        var reponse = await vendeur.Client.PostAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/stores",
            new { name = nom, contactPhone = "+22997000001", contactEmail = (string?)null });

        reponse.EnsureSuccessStatusCode();

        return await LireIdAsync(reponse);
    }

    /// <summary>Un client d'administration : la gouvernance du §22 exige le rôle.</summary>
    public static HttpClient Administration(MerchantsIntegrationFixture fixture)
        => fixture.CreateClientWithToken(TestTokens.Create(ApiAuthorization.AdminRole));

    /// <summary>
    /// LA RÉPONSE EST ENVELOPPÉE : L'IDENTIFIANT EST DANS `data`, PAS À LA RACINE.
    ///
    /// C'est le mode de panne exact trouvé dans `CatalogClient` au lot 6 : lire la
    /// racine ne lève pas, cela rend simplement un GUID vide, et le test échoue
    /// bien plus loin — sur un vendeur introuvable — sans dire pourquoi.
    /// </summary>
    private static async Task<Guid> LireIdAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        if (!corps.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException(
                $"Réponse hors enveloppe §25 : {corps}");
        }

        return data.GetProperty("id").GetGuid();
    }
}
