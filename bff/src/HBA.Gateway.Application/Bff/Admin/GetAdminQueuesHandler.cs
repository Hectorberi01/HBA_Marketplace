using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>Compte les dix files d'attente d'administration, en parallèle.</summary>
public sealed class GetAdminQueuesHandler
{
    /// <summary>
    /// Une file : sa clé, son libellé, le service amont, le chemin, et comment lire
    /// le nombre dans la réponse.
    /// </summary>
    private sealed record File(
        string Cle, string Libelle, string Service, string Chemin, bool Paginee);

    /// <summary>`pageSize=1` SUR LA SEULE FILE PAGINÉE.</summary>
    private static readonly File[] _files =
    [
        // `InReview`, et surtout pas `Pending` : voir l'encadré de la classe.
        new("kyb", "Dossiers vendeurs à vérifier",
            "Merchant", "/api/v1/merchants?kybStatus=InReview&page=1&pageSize=1", true),

        new("produits", "Fiches produits à valider",
            "Catalog", "/api/v1/catalog/admin/products/reviews", false),

        new("marques", "Demandes de marque",
            "Catalog", "/api/v1/catalog/admin/brands/requests", false),

        new("restaurants", "Restaurants à ouvrir",
            "Food", "/api/food/admin/restaurants/pending", false),

        new("livreurs", "Livreurs à vérifier",
            "Drivers", "/api/v1/admin/drivers", false),

        // LES CINQ FILES AJOUTÉES — ET CE QUI LES A RENDUES NÉCESSAIRES.

        // ORDER-SERVICE REND UNE PAGE NUE, PAS L'ENVELOPPE DU §5.
        new("commandes-arbitrage", "Commandes en arbitrage",
            "Order", "/api/admin/orders?status=UnderReview&page=1&pageSize=1", true),

        new("commandes-echec", "Commandes échouées",
            "Order", "/api/admin/orders?status=Failed&page=1&pageSize=1", true),

        // `PendingVerification` ET NON `Suspended` : un compte suspendu est une
        // décision déjà prise, pas une file.
        new("comptes", "Comptes à approuver",
            "Identity", "/api/identity/users?status=PendingVerification&page=1&pageSize=1", true),

        new("factures", "Factures émises, non payées",
            "Financial", "/api/financial/invoices?status=Issued&page=1&pageSize=1", true),

        // LISTE BORNÉE : `ListLowStockQuery(take)` rend au plus `take` articles et
        // ne dit pas combien il en reste.
        new("stock", "Articles sous seuil",
            "Inventory", "/api/inventory/low-stock?take=200", false),
    ];

    /*
     * ═════════════════════════════════════════════════════════════════════════
     * DEUX FAMILLES DE FILES SONT DÉLIBÉRÉMENT ABSENTES.
     *
     * LES RETOURS — arbitrage manuel, inspection à faire, remboursement à faire.
     * `return-refund-service` N'EST PAS DÉPLOYÉ : `ComposeProd.Bloques` le
     * retient parce que « deux adaptateurs gRPC restent des bouchons — la
     * marchandise retournée n'est jamais remise en stock, et aucune course
     * d'enlèvement n'est créée alors qu'un numéro est rendu au client ». Les
     * ajouter donnerait trois files à `null` en permanence, donc trois
     * avertissements permanents dans l'enveloppe — et un avertissement permanent
     * apprend à ignorer les avertissements. Elles s'ajouteront le jour où le
     * service sera déployé, en trois lignes.
     *
     * LES RÈGLEMENTS — versements refusés, lots partiellement échoués.
     * `GET /api/financial/settlements` rend la liste COMPLÈTE des lots, sans
     * aucun paramètre : ni page, ni filtre de statut. Compter « ce qui a échoué »
     * suppose donc de parcourir les lots ET leurs versements, c'est-à-dire une
     * logique métier que ce comptage générique ne porte pas — et qu'il ne doit
     * pas porter, sous peine de devenir le second endroit où l'on décide ce
     * qu'est un règlement en souffrance. La bonne correction est amont : un
     * `?status=` sur cette route, comme en ont ses trois voisines.
     * ═════════════════════════════════════════════════════════════════════════
     */

    private readonly IServiceClientRegistry _services;

    public GetAdminQueuesHandler(IServiceClientRegistry services) => _services = services;

    public async Task<BffEnvelope<AdminQueuesDto>> HandleAsync(CancellationToken cancellationToken)
    {
        using var ctx = AggregationContext.Start("admin.queues");

        // ON LANCE LES CINQ, PUIS ON ATTEND — voir l'encadré d'AggregationContext.
        var appels = _files
            .Select(f => ctx.CallAsync(f.Service, () => CompterAsync(f, cancellationToken)))
            .ToArray();

        await Task.WhenAll(appels);

        var resultats = new List<AdminQueueDto>(_files.Length);

        for (var i = 0; i < _files.Length; i++)
        {
            var file = _files[i];
            var compte = ctx.Resolve(DependencyCriticality.Important, file.Service, await appels[i]);

            resultats.Add(new AdminQueueDto(
                file.Cle,
                file.Libelle,
                compte?.Total,
                compte?.Approximatif ?? false));
        }

        return ctx.Complete(new AdminQueuesDto(resultats));
    }

    private sealed record Compte(int Total, bool Approximatif);

    /// <summary>Interroge un amont et en extrait un nombre.</summary>
    private async Task<ServiceResult<Compte>> CompterAsync(File file, CancellationToken cancellationToken)
    {
        var client = _services.Find(file.Service);

        if (client is null)
        {
            // Clé inconnue = faute de configuration de la passerelle.
            return ServiceResult<Compte>.Failure(501, $"Service inconnu : {file.Service}.");
        }

        var reponse = await client.GetJsonAsync(file.Chemin, cancellationToken);

        if (!reponse.IsSuccess || reponse.Payload is not { } charge)
        {
            return ServiceResult<Compte>.Failure(
                reponse.StatusCode, reponse.FailureReason ?? "Réponse vide.");
        }

        return Extraire(charge, file.Paginee) is { } compte
            ? ServiceResult<Compte>.Success(reponse.StatusCode, compte)
            : ServiceResult<Compte>.Failure(reponse.StatusCode, "Enveloppe amont non reconnue.");
    }

    /// <summary>Lit `meta.total` d'une page, ou la longueur de `data` d'une liste.</summary>
    private static Compte? Extraire(JsonElement charge, bool paginee)
    {
        if (paginee)
        {
            // DEUX FORMES DE PAGE COEXISTENT DANS LA PLATEFORME, ET CE COMPTAGE
            // N'EN CONNAISSAIT QU'UNE.
            if (charge.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (charge.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("total", out var totalEnveloppe)
                && totalEnveloppe.TryGetInt32(out var valeurEnveloppe))
            {
                return new Compte(valeurEnveloppe, Approximatif: false);
            }

            if (charge.TryGetProperty("total", out var totalNu)
                && totalNu.TryGetInt32(out var valeurNue))
            {
                return new Compte(valeurNue, Approximatif: false);
            }

            return null;
        }

        var tableau = charge.ValueKind switch
        {
            JsonValueKind.Array => charge,
            JsonValueKind.Object when charge.TryGetProperty("data", out var data)
                                      && data.ValueKind == JsonValueKind.Array => data,
            _ => (JsonElement?)null,
        };

        // `Approximatif: true` PARCE QUE L'AMONT PLAFONNE.
        return tableau is { } liste
            ? new Compte(liste.GetArrayLength(), Approximatif: true)
            : null;
    }
}
