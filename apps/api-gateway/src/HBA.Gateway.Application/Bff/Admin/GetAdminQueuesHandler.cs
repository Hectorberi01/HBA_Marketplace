using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Shared;

namespace HBA.Gateway.Application.Bff.Admin;

/// <summary>
/// Compte les dix files d'attente d'administration, en parallèle.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CINQ CHEMINS ÉCRITS EN DUR, ET C'EST LE CONTRAIRE D'UN RACCOURCI.
///
/// Le contrat d'`IServiceClient` est explicite : « les chemins passés ici ne
/// doivent JAMAIS venir du client HTTP » — y brancher une valeur de la requête
/// entrante ferait de la passerelle un proxy ouvert vers le réseau interne. Ces
/// dix chemins sont donc des constantes de compilation, sans un seul segment
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
/// neuf autres files restent lisibles et l'administrateur travaille. Celle qui
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

        // ═════════════════════════════════════════════════════════════════════
        // LES CINQ FILES AJOUTÉES — ET CE QUI LES A RENDUES NÉCESSAIRES.
        //
        // Le portail d'administration comptait TREIZE files depuis le
        // NAVIGATEUR, avec `allSettled`, un drapeau d'exactitude et une liste
        // d'échecs : la conception exacte de ce fichier, réinventée du mauvais
        // côté du réseau. Treize allers-retours HTTP, chacun avec son jeton et
        // sa latence, contre dix appels sur le réseau interne.
        //
        // Ces cinq-là s'ajoutent parce qu'aucun service ne peut les rendre au milieu
        // milieu des autres — c'est le critère de ce contrôleur, pas le nombre.
        // ═════════════════════════════════════════════════════════════════════

        // ORDER-SERVICE REND UNE PAGE NUE, PAS L'ENVELOPPE DU §5.
        //
        // `Results.Ok(pagedResult)` pose `total` À LA RACINE ; les cinq files
        // au-dessus lisent `meta.total`. C'est ce qui a obligé `Extraire` à
        // accepter les deux formes — voir son encadré. Ajouter ces deux lignes
        // sans cela les aurait rendues « indisponibles » en permanence, sur un
        // service parfaitement sain.
        new("commandes-arbitrage", "Commandes en arbitrage",
            "Order", "/api/admin/orders?status=UnderReview&page=1&pageSize=1", true),

        new("commandes-echec", "Commandes échouées",
            "Order", "/api/admin/orders?status=Failed&page=1&pageSize=1", true),

        // `PendingVerification` ET NON `Suspended` : un compte suspendu est une
        // décision déjà prise, pas une file. Celui-ci attend un geste — et
        // depuis la mise en production, il ne pouvait rien faire d'autre
        // qu'attendre : aucune route ne l'approuvait.
        new("comptes", "Comptes à approuver",
            "Identity", "/api/identity/users?status=PendingVerification&page=1&pageSize=1", true),

        new("factures", "Factures émises, non payées",
            "Financial", "/api/financial/invoices?status=Issued&page=1&pageSize=1", true),

        // LISTE BORNÉE : `ListLowStockQuery(take)` rend au plus `take` articles
        // et ne dit pas combien il en reste. `Approximatif` sera donc vrai, et
        // l'écran doit écrire « 200+ ». La borne est haute exprès : un stock
        // sous seuil qui dépasse deux cents articles est déjà l'incident.
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
        // Enchaîner `await` par file rendrait l'écran d'accueil aussi lent que la
        // SOMME des services interrogés, au lieu du plus lent d'entre eux.
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
            // ═════════════════════════════════════════════════════════════════
            // DEUX FORMES DE PAGE COEXISTENT DANS LA PLATEFORME, ET CE COMPTAGE
            //     N'EN CONNAISSAIT QU'UNE.
            //
            //   ApiResults.Page(...)      ->  { data, meta: { total, … } }
            //   Results.Ok(pagedResult)   ->  { items, total, page, … }
            //
            // `ApiResults.cs` décrit cette coexistence et la juge : « un endpoint
            // non migré rend encore l'ancienne forme en succès et la nouvelle en
            // erreur. Cette incohérence est TEMPORAIRE et doit être suivie :
            // c'est le pire état des deux mondes. »
            //
            // Les cinq files d'origine tapaient toutes des endpoints migrés, et
            // le comptage n'avait donc jamais rencontré l'autre forme.
            // `/api/admin/orders` la rend, et une file « indisponible » sur un
            // service parfaitement sain aurait envoyé chercher la panne dans
            // order-service — c'est-à-dire au mauvais endroit.
            //
            // ON LIT `meta.total` D'ABORD. Une page enveloppée porte aussi un
            // `data`, jamais un `total` à la racine ; l'ordre n'est donc pas
            // ambigu, il est seulement explicite.
            //
            // Le jour où toute la plateforme aura migré, c'est la seconde branche
            // qui disparaîtra — et ce sera le seul endroit à toucher.
            // ═════════════════════════════════════════════════════════════════
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
        //
        // `admin/drivers` rend au plus `take` éléments (100 par défaut) et ne dit
        // pas combien il en reste. Compter ce qu'on reçoit est donc un PLANCHER,
        // pas un total — l'écran doit écrire « 100+ », jamais « 100 ».
        return tableau is { } liste
            ? new Compte(liste.GetArrayLength(), Approximatif: true)
            : null;
    }
}
