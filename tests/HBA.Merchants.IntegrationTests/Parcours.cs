using System.Net.Http.Json;
using System.Text.Json;
using HBA.Shared.Hosting.Http;
using HBA.Tests.Authorization;

namespace HBA.Merchants.IntegrationTests;

/// <summary>
/// Un vendeur inscrit par le parcours réel, avec de quoi continuer à agir en son
/// nom.
/// </summary>
internal sealed record VendeurInscrit(Guid UserId, Guid SellerId, HttpClient Client);

/// <summary>LES GESTES DU PARCOURS, PASSÉS PAR LA VRAIE SURFACE HTTP.</summary>
internal static class Parcours
{
    
    
    /// <summary>Inscrit un vendeur et rend de quoi agir en son nom.</summary>
    public static async Task<VendeurInscrit> InscrireAsync(
        MerchantsIntegrationFixture fixture, string nomBoutique)
    {
        var userId = Guid.NewGuid();
        var client = fixture.CreateClientWithToken(
            TestTokens.Create(userId, ApiAuthorization.SellerRole));

        var reponse = await client.PostAsJsonAsync(
            "/api/v1/merchants",
            new { shopName = nomBoutique, commissionRate = 0.10m });

        await ReussirAsync(reponse);

        return new VendeurInscrit(userId, await LireIdAsync(reponse), client);
    }

    /// <summary>
    /// Téléverse un fichier au nom du vendeur, puis le rattache à son dossier KYB.
    /// Bascule le dossier en revue (comportement déprécié).
    /// </summary>
    public static async Task<Guid> DeposerPieceAsync(
        MerchantsIntegrationFixture fixture, VendeurInscrit vendeur, string type = "IdCard")
    {
        var mediaId = fixture.Media.Deposer(
            vendeur.SellerId, deposeParUserId: vendeur.UserId);

        var reponse = await RattacherPieceAsync(vendeur, mediaId, type);

        await ReussirAsync(reponse);

        return await LireIdAsync(reponse);
    }

    /// <summary>Rattache un média BRUT, sans exiger le succès — pour éprouver les refus.</summary>
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

        await ReussirAsync(reponse);
    }

    /// <summary>Le MÊME vendeur, mais avec un jeton dont l'authentification a vieilli.</summary>
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

        await ReussirAsync(reponse);
    }

    /// <summary>Crée une boutique pour ce vendeur.</summary>
    public static async Task<Guid> CreerBoutiqueAsync(VendeurInscrit vendeur, string nom)
    {
        var reponse = await vendeur.Client.PostAsJsonAsync(
            $"/api/v1/merchants/{vendeur.SellerId}/stores",
            new { name = nom, contactPhone = "+22997000001", contactEmail = (string?)null });

        await ReussirAsync(reponse);

        return await LireIdAsync(reponse);
    }

    /// <summary>Exige le succès, et DIT POURQUOI quand il n'est pas au rendez-vous.</summary>
    private static async Task ReussirAsync(HttpResponseMessage reponse)
    {
        if (reponse.IsSuccessStatusCode)
        {
            return;
        }

        var defi = reponse.Headers.WwwAuthenticate.Count > 0
            ? string.Join(" | ", reponse.Headers.WwwAuthenticate)
            : "(aucun en-tête WWW-Authenticate — le refus ne vient pas du gestionnaire de jeton)";

        var corps = await reponse.Content.ReadAsStringAsync();

        // LU AVANT LA TRONCATURE : sur un corps long, l'identifiant tomberait dans
        // les caractères jetés et l'on ne saurait plus relier la réponse à
        // l'exception qui l'a produite.
        var correlation = LireCorrelation(corps);

        if (corps.Length > 2000)
        {
            corps = corps[..2000] + " […]";
        }

        throw new HttpRequestException(
            $"{(int)reponse.StatusCode} {reponse.StatusCode} sur "
            + $"{reponse.RequestMessage?.Method} {reponse.RequestMessage?.RequestUri}"
            + $"\n  WWW-Authenticate : {defi}"
            + $"\n  corps            : {corps}"
            + JournalHote.Pour(correlation));
    }

    /// <summary>L'identifiant de corrélation d'une réponse d'erreur, s'il s'y trouve.</summary>
    private static string? LireCorrelation(string corps)
    {
        try
        {
            using var document = JsonDocument.Parse(corps);
            var racine = document.RootElement;

            if (racine.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (racine.TryGetProperty("correlationId", out var direct)
                && direct.ValueKind is JsonValueKind.String)
            {
                return direct.GetString();
            }

            if (racine.TryGetProperty("meta", out var meta)
                && meta.ValueKind is JsonValueKind.Object
                && meta.TryGetProperty("requestId", out var demande)
                && demande.ValueKind is JsonValueKind.String)
            {
                return demande.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Un client d'administration : la gouvernance du §22 exige le rôle.</summary>
    public static HttpClient Administration(MerchantsIntegrationFixture fixture)
        => fixture.CreateClientWithToken(TestTokens.Create(ApiAuthorization.AdminRole));

    /// <summary>
    /// LA RÉPONSE EST ENVELOPPÉE : L'IDENTIFIANT EST DANS `data`, PAS À LA RACINE.
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
