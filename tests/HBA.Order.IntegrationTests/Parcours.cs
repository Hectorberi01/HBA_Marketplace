using System.Net.Http.Json;
using System.Text.Json;
using HBA.Tests.Authorization;

namespace HBA.Order.IntegrationTests;

/// <summary>
/// Une commande passée par le parcours réel, avec de quoi vérifier ce qu'elle
/// devient.
/// </summary>
/// <param name="LieuExpedition">
/// Le lieu d'expédition unique de toutes ses lignes : c'est le couple (SKU, lieu,
/// commande) qu'`InventaireDeTest` enregistre.
/// </param>
internal sealed record CommandePassee(
    Guid AcheteurId,
    Guid CommandeId,
    Guid LieuExpedition,
    IReadOnlyList<string> Skus);

/// <summary>LES GESTES DU PARCOURS, PASSÉS PAR LA VRAIE SURFACE HTTP.</summary>
internal static class Parcours
{
    /// <summary>Passe une commande de marchandise et rend de quoi la suivre.</summary>
    /// <param name="skus">
    /// Une ligne par SKU. Deux par défaut : c'est le minimum pour que « une
    /// libération PAR LIGNE » se distingue de « au moins une libération ».
    /// </param>
    public static async Task<CommandePassee> PasserCommandeAsync(
        OrderIntegrationFixture fixture, params string[] skus)
    {
        if (skus.Length == 0)
        {
            skus = [$"SKU-A-{Guid.NewGuid():N}", $"SKU-B-{Guid.NewGuid():N}"];
        }

        var acheteurId = Guid.NewGuid();

        // AUCUN RÔLE : `/api/orders` passe par `MapAuthenticatedGroup`, et c'est
        // correct — la propriété de la commande se vérifie DANS le gestionnaire,
        // pas au niveau du groupe.
        var client = fixture.CreateClientWithToken(TestTokens.Create(acheteurId));

        var lieu = fixture.Inventaire.DeposerLieu();
        fixture.Panier.Deposer(acheteurId, lieu, skus);

        var reponse = await client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                // COMMUNE ET POINT DE REPÈRE, PAS DE RUE.
                shippingAddress = new
                {
                    label = "Maison",
                    recipient = "Kossi Adjovi",
                    phone = "+22997000001",
                    communeCode = "cotonou",
                    quartier = "Fidjrosse",
                    landmark = "Après le carrefour de la SBEE",
                    line1 = (string?)null,
                    countryCode = "BJ",
                    latitude = (double?)null,
                    longitude = (double?)null
                },

                // AUCUN DEVIS DE COURSE, ET C'EST UN CHOIX EXPLIQUÉ DANS
                // `CourseDeTest`.
                deliveryQuoteId = (string?)null
            });

        reponse.EnsureSuccessStatusCode();

        return new CommandePassee(acheteurId, await LireIdAsync(reponse), lieu, skus);
    }

    /// <summary>Prépare une commande et rend de quoi la déclencher, succès ou refus.</summary>
    public static (Guid AcheteurId, IReadOnlyList<Guid> Offres, Func<Task<HttpResponseMessage>> Commander)
        PreparerCommande(OrderIntegrationFixture fixture, params string[] skus)
    {
        if (skus.Length == 0)
        {
            skus = [$"SKU-A-{Guid.NewGuid():N}"];
        }

        var acheteurId = Guid.NewGuid();
        var client = fixture.CreateClientWithToken(TestTokens.Create(acheteurId));

        var lieu = fixture.Inventaire.DeposerLieu();
        fixture.Panier.Deposer(acheteurId, lieu, skus);

        return (acheteurId, fixture.Panier.Offres(acheteurId), () => client.PostAsJsonAsync(
            "/api/orders",
            new
            {
                shippingAddress = new
                {
                    label = "Maison",
                    recipient = "Kossi Adjovi",
                    phone = "+22997000001",
                    communeCode = "cotonou",
                    quartier = "Fidjrosse",
                    landmark = "Après le carrefour de la SBEE",
                    line1 = (string?)null,
                    countryCode = "BJ",
                    latitude = (double?)null,
                    longitude = (double?)null
                },
                deliveryQuoteId = (string?)null
            }));
    }

    /// <summary>Le code métier porté par une réponse de refus.</summary>
    public static async Task<string> LireLeCodeMetierAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        if (corps.ValueKind != JsonValueKind.Object
            || !corps.TryGetProperty("error", out var erreur)
            || erreur.ValueKind != JsonValueKind.Object
            || !erreur.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Réponse de refus qui ne suit pas l'enveloppe du §5 (`error.details[]`) : "
                + $"{corps}");
        }

        foreach (var detail in details.EnumerateArray())
        {
            if (detail.ValueKind == JsonValueKind.Object
                && detail.TryGetProperty("field", out var champ)
                && champ.ValueKind == JsonValueKind.String
                && champ.GetString() == "reason"
                && detail.TryGetProperty("message", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                return code.GetString()!;
            }
        }

        // ON LÈVE PLUTÔT QUE DE RENDRE `null`.
        throw new InvalidOperationException(
            $"Aucun détail `reason` dans la réponse de refus : {corps}");
    }

    /// <summary>CETTE RÉPONSE-CI N'EST PAS ENVELOPPÉE, CONTRAIREMENT À LA PLUPART.</summary>
    private static async Task<Guid> LireIdAsync(HttpResponseMessage reponse)
    {
        var corps = await reponse.Content.ReadFromJsonAsync<JsonElement>();

        // `TryGetProperty` LÈVE sur autre chose qu'un objet JSON : on ne veut pas
        // d'une `InvalidOperationException` nue là où le corps reçu doit être dit.
        var porteur = corps.ValueKind == JsonValueKind.Object
                      && corps.TryGetProperty("data", out var data)
            ? data
            : corps;

        if (porteur.ValueKind != JsonValueKind.Object
            || !porteur.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Réponse de POST /api/orders sans identifiant lisible : {corps}");
        }

        return id.GetGuid();
    }
}
