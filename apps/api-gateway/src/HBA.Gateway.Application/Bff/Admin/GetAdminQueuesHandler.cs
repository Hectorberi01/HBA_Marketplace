using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>
/// Compte les cinq files d'attente d'administration, en parallèle.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CINQ CHEMINS ÉCRITS EN DUR, ET C'EST LE CONTRAIRE D'UN RACCOURCI.
///
/// Le contrat d'`IServiceClient` est explicite : « les chemins passés ici ne
/// doivent JAMAIS venir du client HTTP » — y brancher une valeur de la requête
/// entrante ferait de la passerelle un proxy ouvert vers le réseau interne. Ces
/// cinq chemins sont donc des constantes de compilation, sans un seul segment
/// interpolé.
///
/// QUATRE SUR CINQ N'ONT AUCUN FILTRE À DEVINER, ET LE CINQUIÈME A COÛTÉ CHER.
///
/// C'est le critère qui a présidé au choix des routes : `brands/requests`,
/// `restaurants/pending` et `products/reviews` portent l'attente DANS LEUR
/// CHEMIN, et `admin/drivers` vaut déjà `UnderReview` sans paramètre. Aucune
/// chaîne d'énumération à deviner.
///
/// LA CINQUIÈME EN A UNE, ET LA PREMIÈRE VERSION L'A DEVINÉE FAUX.
///
/// `?kybStatus=Pending` avait été écrit ici par analogie avec `SellerStatus`, qui
/// possède bien un `Pending`. `KybStatus`, lui, ne l'a pas : ses valeurs sont
/// `NotStarted`, `InReview`, `Verified`, `Rejected`. La bonne est `InReview`.
///
/// ET LE SYMPTÔME AURAIT ÉTÉ PIRE QU'UN ZÉRO. `ListSellersQueryHandler` documente
/// son propre choix — « un filtre illisible est IGNORÉ, pas refusé ». La valeur
/// erronée n'aurait donc pas vidé la file : elle aurait supprimé le filtre, et la
/// tuile « Dossiers vendeurs à vérifier » aurait affiché le NOMBRE TOTAL DE
/// VENDEURS de la plateforme, présenté comme un compte exact.
///
/// Un compteur faux est pire qu'un compteur absent : il est silencieux.
///
/// TOUTES LES FILES SONT `Important`, AUCUNE N'EST `Critical`.
///
/// Un service à terre ne doit pas coûter l'écran d'accueil tout entier : les
/// quatre autres files restent lisibles et l'administrateur travaille. Celle qui
/// manque s'affiche « indisponible » — voir `AdminQueueDto.Total`.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class GetAdminQueuesHandler
{
    /// <summary>
    /// Une file : sa clé, son libellé, le service amont, le chemin, et comment
    /// lire le nombre dans la réponse.
    /// </summary>
    private sealed record File(
        string Cle, string Libelle, string Service, string Chemin, bool Paginee);

    /// <summary>
    /// `pageSize=1` SUR LA SEULE FILE PAGINÉE.
    ///
    /// On veut `meta.total`, pas les éléments. Demander la page par défaut ferait
    /// transiter vingt dossiers vendeurs complets — à chaque ouverture, par chaque
    /// administrateur — pour n'en lire qu'un entier. `pageSize=0` n'est pas
    /// retenu : rien ne garantit que l'amont l'accepte, et un 400 ferait
    /// disparaître la file.
    /// </summary>
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
    ];

    private readonly IServiceClientRegistry _services;

    public GetAdminQueuesHandler(IServiceClientRegistry services) => _services = services;

    public async Task<BffEnvelope<AdminQueuesDto>> HandleAsync(CancellationToken cancellationToken)
    {
        using var ctx = AggregationContext.Start("admin.queues");

        // ON LANCE LES CINQ, PUIS ON ATTEND — voir l'encadré d'AggregationContext.
        // Enchaîner `await` par file rendrait l'écran d'accueil aussi lent que la
        // SOMME des cinq services, au lieu du plus lent d'entre eux.
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

    /// <summary>
    /// Interroge un amont et en extrait un nombre.
    /// </summary>
    /// <remarks>
    /// UNE RÉPONSE ILLISIBLE EST UN ÉCHEC, PAS UN ZÉRO.
    ///
    /// Si l'enveloppe change de forme — `meta.total` renommé, `data` qui cesse
    /// d'être un tableau — le comptage doit rendre `Failure`, donc « indisponible »
    /// à l'écran. Le repli sur `0` transformerait un changement de contrat amont en
    /// file vide, et personne ne le remarquerait avant qu'un vendeur ne se plaigne
    /// d'attendre depuis trois semaines.
    /// </remarks>
    private async Task<ServiceResult<Compte>> CompterAsync(File file, CancellationToken cancellationToken)
    {
        var client = _services.Find(file.Service);

        if (client is null)
        {
            // Clé inconnue = faute de configuration de la passerelle. 501 plutôt
            // que 503 : `AggregationContext` le traduit en `NOT_CONFIGURED`, donc
            // en « ce bloc ne reviendra pas » côté client, et non en « réessayez ».
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

    /// <summary>
    /// Lit `meta.total` d'une page, ou la longueur de `data` d'une liste.
    /// </summary>
    /// <remarks>
    /// Les deux formes viennent de `ApiResults` : `Page` pose `meta.total`, `Ok`
    /// pose `data`. On accepte aussi un tableau nu, parce que `driver-service`
    /// répond par `Results.Ok` et non par `ApiResults.Ok` — un écart réel du
    /// dépôt, qu'il vaut mieux absorber ici que faire échouer une file.
    /// </remarks>
    private static Compte? Extraire(JsonElement charge, bool paginee)
    {
        if (paginee)
        {
            if (charge.ValueKind == JsonValueKind.Object
                && charge.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("total", out var total)
                && total.TryGetInt32(out var valeur))
            {
                return new Compte(valeur, Approximatif: false);
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
        //
        // `admin/drivers` rend au plus `take` éléments (100 par défaut) et ne dit
        // pas combien il en reste. Compter ce qu'on reçoit est donc un PLANCHER,
        // pas un total — l'écran doit écrire « 100+ », jamais « 100 ».
        return tableau is { } liste
            ? new Compte(liste.GetArrayLength(), Approximatif: true)
            : null;
    }
}
