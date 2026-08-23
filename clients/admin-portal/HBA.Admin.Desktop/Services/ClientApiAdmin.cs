using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HBA.Admin.Desktop.Services;

/// <summary>Le seul point par lequel l'application parle à la passerelle.</summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SEUL ENDROIT QUI POSE L'EN-TÊTE `Authorization`, ET C'EST LE POINT.
///
/// Chaque écran qui construirait sa propre requête devrait penser au jeton, à son
/// expiration, et au rafraîchissement. Le premier qui l'oublie produit un 401 que
/// l'on prend pour une session expirée — et l'on cherche du côté de la connexion
/// un défaut qui est dans un écran.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ClientApiAdmin : IDisposable
{
    private const string Auth = "/api/v1/auth";
    private const string Bff = "/api/v1/bff/admin";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SessionAdmin _session;

    public ClientApiAdmin(ConfigurationAdmin configuration, SessionAdmin session)
    {
        _session = session;

        _http = new HttpClient
        {
            BaseAddress = new Uri(configuration.UrlPasserelle, UriKind.Absolute),

            // DÉLAI COURT, ET ASSUMÉ.
            //
            // Un poste d'administration attend une réponse ou un message, jamais
            // une roue qui tourne. Les cent secondes par défaut de `HttpClient`
            // donnent, sur un service à terre, une minute et demie d'interface
            // figée — pendant laquelle l'administrateur reclique.
            Timeout = TimeSpan.FromSeconds(15),
        };

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Ouvre une session.</summary>
    /// <remarks>
    /// LE SECOND FACTEUR EST EXIGÉ PAR LE SERVEUR, PAS PAR CET ÉCRAN.
    ///
    /// L'application ne décide pas qu'un compte doit passer par la MFA : elle
    /// envoie ce qu'elle a et lit `mfaRequired`. Un client qui déciderait
    /// lui-même serait contournable en modifiant le client.
    /// </remarks>
    public async Task<Resultat<IssueConnexion>> ConnecterAsync(
        string courriel, string motDePasse, string? code, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"{Auth}/login",
            new { email = courriel, password = motDePasse, mfaCode = code }, authentifier: false, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IssueConnexion>.Echec(reponse.Message!);
        }

        var corps = Lire<ReponseConnexion>(reponse.Valeur);

        if (corps is null)
        {
            return Resultat<IssueConnexion>.Echec("Réponse de connexion illisible.");
        }

        // ON TESTE LES JETONS, PAS SEULEMENT `mfaRequired`.
        //
        // Un compte à second facteur qui fournit un code correct reçoit
        // `mfaRequired = true` ET des jetons dans la même réponse. Se fier au seul
        // drapeau redemanderait le code indéfiniment, en boucle, à chaque saisie
        // correcte.
        if (corps.Jetons is { AccessToken: not null, RefreshToken: not null } jetons)
        {
            Poser(jetons);
            return Resultat<IssueConnexion>.Ok(IssueConnexion.Ouverte);
        }

        return corps.MfaRequise
            ? Resultat<IssueConnexion>.Ok(IssueConnexion.CodeExige)
            : Resultat<IssueConnexion>.Echec("Identifiants refusés.");
    }

    /// <summary>
    /// Rejoue le mot de passe pour élever la session avant un geste sensible.
    /// </summary>
    /// <remarks>
    /// Le serveur rend une NOUVELLE paire de jetons, portant un `auth_time` neuf.
    /// C'est cette paire qu'il faut poser : garder l'ancienne laisserait le geste
    /// suivant se faire refuser alors que le mot de passe vient d'être saisi.
    /// </remarks>
    public async Task<Resultat<bool>> EleverAsync(string motDePasse, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"{Auth}/reauthenticate",
            new { password = motDePasse }, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<bool>.Echec(reponse.Message!);
        }

        var jetons = Lire<JetonsApi>(reponse.Valeur);

        if (jetons is not { AccessToken: not null, RefreshToken: not null })
        {
            return Resultat<bool>.Echec("Réponse de ré-authentification illisible.");
        }

        Poser(jetons);
        return Resultat<bool>.Ok(true);
    }

    /// <summary>Les files d'attente d'administration.</summary>
    public async Task<Resultat<EnveloppeBff<FilesDAttente>>> LireFilesAsync(CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(HttpMethod.Get, $"{Bff}/queues", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<EnveloppeBff<FilesDAttente>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeBff<FilesDAttente>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<EnveloppeBff<FilesDAttente>>.Echec("Réponse des files illisible.")
            : Resultat<EnveloppeBff<FilesDAttente>>.Ok(enveloppe);
    }

    /// <summary>Liste les vendeurs, filtrée et paginée.</summary>
    /// <remarks>
    /// LES FILTRES SONT ENCODÉS, PAS CONCATÉNÉS.
    ///
    /// Un nom de boutique contenant `&amp;` — ce qui arrive — couperait la chaîne de
    /// requête en deux et ferait chercher un filtre inexistant. Le service
    /// répondrait alors une liste complète au lieu d'une recherche, sans erreur.
    /// </remarks>
    public async Task<Resultat<PageApi<VendeurAdmin>>> ListerVendeursAsync(
        int page, int taille, string? recherche, string? kyb, string? statut,
        CancellationToken jeton = default)
    {
        var parametres = new List<string>
        {
            $"page={page}",
            $"pageSize={taille}",
        };

        if (!string.IsNullOrWhiteSpace(recherche))
        {
            parametres.Add($"search={Uri.EscapeDataString(recherche.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(kyb))
        {
            parametres.Add($"kybStatus={Uri.EscapeDataString(kyb)}");
        }

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/v1/merchants?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<VendeurAdmin>>.Echec(reponse.Message!);
        }

        var page2 = Lire<PageApi<VendeurAdmin>>(reponse.Valeur);

        return page2?.Data is null
            ? Resultat<PageApi<VendeurAdmin>>.Echec("Liste des vendeurs illisible.")
            : Resultat<PageApi<VendeurAdmin>>.Ok(page2);
    }

    /// <summary>Applique un geste de gouvernance à un vendeur.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES GESTES RENDENT 204, DONC L'ABSENCE DE CORPS EST LE SUCCÈS.
    ///
    /// merchant-service répond `NoContent` sur les six. Un client qui tenterait
    /// de désérialiser la réponse conclurait à un échec sur chaque geste RÉUSSI —
    /// et l'administrateur recommencerait, sur des routes dont toutes ne sont pas
    /// idempotentes.
    ///
    /// LE MOTIF EST ENVOYÉ SEULEMENT QUAND LE GESTE L'EXIGE.
    ///
    /// L'ajouter partout serait sans effet apparent — le service ignore un champ
    /// qu'il ne lit pas — jusqu'au jour où l'un d'eux le lira. C'est la table
    /// `GesteVendeur` qui décide, pas l'écran.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> AgirSurVendeurAsync(
        Guid vendeur, GesteVendeur geste, string? motif, CancellationToken jeton = default)
    {
        var corps = geste.MotifExige ? new { reason = motif ?? string.Empty } : null;

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/merchants/{vendeur}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les retraits d'une file donnée.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CES ROUTES RENDENT UN TABLEAU NU, PAS UNE ENVELOPPE.
    ///
    /// `ListPendingWithdrawalsAsync` répond par `Results.Ok(...)` et non par
    /// `ApiResults.Ok(...)` : il n'y a ni `data`, ni `meta`, ni pagination. Les
    /// désérialiser comme une `PageApi` rendrait `Data` nul — donc une file vide
    /// et silencieuse, sur l'écran où l'on décide de faire sortir de l'argent.
    ///
    /// ET IL N'Y A DONC AUCUNE PAGINATION.
    ///
    /// Le service rend tout ce qui attend. C'est tenable tant que la file se vide
    /// chaque jour ; ça cessera de l'être le jour où elle ne se videra plus — et
    /// ce jour-là, c'est côté serveur qu'il faudra agir.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<T>>> ListerRetraitsAsync<T>(
        FileRetraits file, CancellationToken jeton = default)
        where T : class
    {
        var chemin = file switch
        {
            FileRetraits.PartenairesEnAttente => "/api/wallet/withdrawals/pending",
            FileRetraits.PartenairesEnCours => "/api/wallet/withdrawals/processing",
            _ => "/api/wallet/customer-withdrawals/pending",
        };

        var reponse = await EnvoyerAsync(HttpMethod.Get, chemin, null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<T>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<T>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<T>>.Echec("File de retraits illisible.")
            : Resultat<IReadOnlyList<T>>.Ok(liste);
    }

    /// <summary>Applique un geste à une demande de retrait.</summary>
    /// <remarks>
    /// LE CORPS DÉPEND DE LA SAISIE EXIGÉE, ET LA TABLE `GesteRetrait` LE DIT.
    ///
    /// `reject` attend `{ reason }`, `paid` attend `{ externalReference }`, et
    /// `approve` n'attend rien. Envoyer le mauvais champ produit un 400 que rien
    /// à l'écran ne rattache au geste.
    /// </remarks>
    public async Task<Resultat<bool>> AgirSurRetraitAsync(
        FileRetraits file, Guid retrait, GesteRetrait geste, string? saisie,
        CancellationToken jeton = default)
    {
        var racine = file == FileRetraits.Clients
            ? "/api/wallet/customer-withdrawals"
            : "/api/wallet/withdrawals";

        object? corps = geste.Saisie switch
        {
            SaisieRequise.Motif => new { reason = saisie ?? string.Empty },
            SaisieRequise.Reference => new { externalReference = saisie ?? string.Empty },
            _ => null,
        };

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"{racine}/{retrait}/{geste.Chemin}", corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les paiements, filtrés et paginés.</summary>
    /// <remarks>
    /// `page` ET `pageSize` SONT OBLIGATOIRES, ET C'EST DANS LA SIGNATURE AMONT.
    ///
    /// `ListPaymentsAsync(int page, int pageSize, …)` les déclare NON nullables,
    /// contrairement à la liste des vendeurs qui les prend en `int?`. Les omettre
    /// ne rend donc pas une page par défaut : la liaison de modèle échoue et le
    /// service répond 400, sur une route qui a pourtant l'air facultative.
    /// </remarks>
    public async Task<Resultat<PageBrute<PaiementAdmin>>> ListerPaiementsAsync(
        int page, int taille, string? recherche, string? statut, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(recherche))
        {
            parametres.Add($"search={Uri.EscapeDataString(recherche.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/payments?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageBrute<PaiementAdmin>>.Echec(reponse.Message!);
        }

        var page2 = Lire<PageBrute<PaiementAdmin>>(reponse.Valeur);

        return page2?.Items is null
            ? Resultat<PageBrute<PaiementAdmin>>.Echec("Liste des paiements illisible.")
            : Resultat<PageBrute<PaiementAdmin>>.Ok(page2);
    }

    /// <summary>Le résumé chiffré de la file de paiements.</summary>
    /// <remarks>
    /// LA MÊME RECHERCHE QUE LA LISTE, SINON LES DEUX SE CONTREDISENT.
    ///
    /// `stats` accepte `search` : sans le lui passer, l'en-tête annoncerait les
    /// totaux de TOUTE la plateforme au-dessus d'une liste filtrée. Deux chiffres
    /// justes côte à côte qui ne parlent pas de la même chose.
    /// </remarks>
    public async Task<Resultat<StatsPaiements>> LireStatsPaiementsAsync(
        string? recherche, CancellationToken jeton = default)
    {
        var requete = string.IsNullOrWhiteSpace(recherche)
            ? "/api/payments/stats"
            : $"/api/payments/stats?search={Uri.EscapeDataString(recherche.Trim())}";

        var reponse = await EnvoyerAsync(HttpMethod.Get, requete, null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<StatsPaiements>.Echec(reponse.Message!);
        }

        var stats = Lire<StatsPaiements>(reponse.Valeur);

        return stats is null
            ? Resultat<StatsPaiements>.Echec("Résumé des paiements illisible.")
            : Resultat<StatsPaiements>.Ok(stats);
    }

    /// <summary>Applique un geste de rattrapage à un paiement.</summary>
    public async Task<Resultat<bool>> AgirSurPaiementAsync(
        Guid paiement, GestePaiement geste, string? saisie, CancellationToken jeton = default)
    {
        object? corps = geste.Saisie switch
        {
            SaisieRequise.Motif => new { reason = saisie ?? string.Empty },
            SaisieRequise.Reference => new { providerReference = saisie ?? string.Empty },
            _ => null,
        };

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/payments/{paiement}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les fiches produits — la file de validation, ou tout le catalogue.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX ROUTES, ET LA FILE N'EST PAS UN FILTRE DE L'AUTRE.
    ///
    /// `products/reviews` rend les fiches en attente de validation ; `products`
    /// rend tout le catalogue, avec recherche et filtre de statut. On pourrait
    /// croire la première équivalente à la seconde avec `status=PendingReview` —
    /// elle ne l'est pas : `AdminReviewQueries` interroge les RÉVISIONS, pas les
    /// fiches, et une fiche peut porter plusieurs révisions successives.
    ///
    /// Les deux rendent la même forme — `ApiResults.Page`, donc `{data, meta}` —
    /// ce qui n'est PAS le cas de la liste des paiements. Voir `PageBrute`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<PageApi<ProduitAdmin>>> ListerProduitsAsync(
        bool fileDeValidation, int page, int taille, string? recherche, string? statut,
        CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        string chemin;

        if (fileDeValidation)
        {
            // La file n'accepte NI recherche NI statut : les lui passer serait
            // sans effet, et donnerait à l'écran l'apparence d'un filtre actif.
            chemin = "/api/v1/catalog/admin/products/reviews";
        }
        else
        {
            chemin = "/api/v1/catalog/admin/products";

            if (!string.IsNullOrWhiteSpace(recherche))
            {
                parametres.Add($"search={Uri.EscapeDataString(recherche.Trim())}");
            }

            if (!string.IsNullOrWhiteSpace(statut))
            {
                parametres.Add($"status={Uri.EscapeDataString(statut)}");
            }
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"{chemin}?{string.Join('&', parametres)}", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<ProduitAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<ProduitAdmin>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<ProduitAdmin>>.Echec("Liste des produits illisible.")
            : Resultat<PageApi<ProduitAdmin>>.Ok(resultat);
    }

    /// <summary>Applique un geste de modération à une fiche produit.</summary>
    /// <remarks>
    /// LE REJET ENVOIE UN MOTIF STRUCTURÉ **ET** UN COMMENTAIRE.
    ///
    /// `RejectRequest(Comment, Reasons)` porte les deux. L'endpoint tolère
    /// `Reasons` nul — `request.Reasons ?? []` — mais un rejet sans motif ne dit
    /// rien au vendeur, qui ne saura pas quoi corriger et resoumettra la même
    /// fiche. On envoie donc le texte saisi aux DEUX places : en commentaire
    /// libre, et comme motif unique de code `ADMIN`.
    /// </remarks>
    public async Task<Resultat<bool>> AgirSurProduitAsync(
        Guid produit, GesteProduit geste, string? saisie, CancellationToken jeton = default)
    {
        object? corps = geste.Cle switch
        {
            "rejeter" => new
            {
                comment = saisie ?? string.Empty,
                reasons = new[] { new MotifRejet("ADMIN", null, saisie ?? string.Empty) },
            },
            "suspendre" => new { reason = saisie ?? string.Empty },
            _ => null,
        };

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/products/{produit}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les commandes de la marketplace, filtrées et paginées.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LES COMMANDES REPAS N'ONT PAS DE LISTE D'ADMINISTRATION.
    ///
    /// `/api/admin/food/orders` n'expose QUE les deux gestes d'arbitrage —
    /// `review/refund` et `review/resume`. Il n'y a pas de `GET`. Une commande
    /// repas bloquée en arbitrage est donc arbitrable, mais introuvable depuis
    /// cette console : il faut connaître son identifiant.
    ///
    /// C'est une route serveur à ouvrir, pas un écran à écrire — et c'est dit à
    /// l'écran plutôt que tu dans le code.
    ///
    /// `page` ET `pageSize` SONT OBLIGATOIRES, comme pour les paiements :
    /// `ListAllAsync(int page, int pageSize, …)` les déclare non nullables.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<PageBrute<CommandeAdmin>>> ListerCommandesAsync(
        int page, int taille, string? recherche, string? statut, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(recherche))
        {
            parametres.Add($"search={Uri.EscapeDataString(recherche.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/admin/orders?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageBrute<CommandeAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageBrute<CommandeAdmin>>(reponse.Valeur);

        return resultat?.Items is null
            ? Resultat<PageBrute<CommandeAdmin>>.Echec("Liste des commandes illisible.")
            : Resultat<PageBrute<CommandeAdmin>>.Ok(resultat);
    }

    /// <summary>Arbitre une commande bloquée.</summary>
    public async Task<Resultat<bool>> AgirSurCommandeAsync(
        Guid commande, GesteCommande geste, string? motif, CancellationToken jeton = default)
    {
        object? corps = geste.Saisie == SaisieRequise.Motif
            ? new { reason = motif ?? string.Empty }
            : null;

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/admin/orders/{commande}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les dossiers de livreur d'un statut donné.</summary>
    /// <remarks>
    /// `take` PLAFONNE, ET LA RÉPONSE NE DIT PAS CE QUI RESTE.
    ///
    /// `ListDriverAccountsQuery(Status, Take = 100)` rend un tableau nu, sans
    /// total ni page suivante. Le compte affiché est donc un PLANCHER dès qu'il
    /// atteint la borne — c'est la même limite que la tuile « Livreurs à
    /// vérifier » de l'accueil, qui écrit `100+`.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<LivreurAdmin>>> ListerLivreursAsync(
        string statut, int plafond, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get,
            $"/api/v1/admin/drivers?status={Uri.EscapeDataString(statut)}&take={plafond}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<LivreurAdmin>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<LivreurAdmin>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<LivreurAdmin>>.Echec("Liste des livreurs illisible.")
            : Resultat<IReadOnlyList<LivreurAdmin>>.Ok(liste);
    }

    /// <summary>Décide d'un dossier de livreur.</summary>
    public async Task<Resultat<bool>> AgirSurLivreurAsync(
        Guid livreur, GesteLivreur geste, string? motif, CancellationToken jeton = default)
    {
        object? corps = geste.Saisie == SaisieRequise.Motif
            ? new { reason = motif ?? string.Empty }
            : null;

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/admin/drivers/{livreur}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les restaurants en attente d'ouverture.</summary>
    /// <remarks>
    /// SEULE LA FILE « EN ATTENTE » EST LISIBLE.
    ///
    /// `/api/food/admin` n'expose que `restaurants/pending`. Suspendre un
    /// restaurant DÉJÀ ouvert est possible — la route existe — mais il faut
    /// connaître son identifiant : aucune liste ne le rend.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<RestaurantAdmin>>> ListerRestaurantsAsync(
        int plafond, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/food/admin/restaurants/pending?take={plafond}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<RestaurantAdmin>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<RestaurantAdmin>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<RestaurantAdmin>>.Echec("Liste des restaurants illisible.")
            : Resultat<IReadOnlyList<RestaurantAdmin>>.Ok(liste);
    }

    /// <summary>Décide de l'ouverture ou de la suspension d'un restaurant.</summary>
    public async Task<Resultat<bool>> AgirSurRestaurantAsync(
        Guid restaurant, GesteRestaurant geste, string? motif, CancellationToken jeton = default)
    {
        object? corps = geste.Saisie == SaisieRequise.Motif
            ? new { reason = motif ?? string.Empty }
            : null;

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/food/admin/restaurants/{restaurant}/{geste.Chemin}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les rôles et leurs permissions.</summary>
    public async Task<Resultat<IReadOnlyList<RoleAdmin>>> ListerRolesAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/identity/roles", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<RoleAdmin>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<RoleAdmin>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<RoleAdmin>>.Echec("Liste des rôles illisible.")
            : Resultat<IReadOnlyList<RoleAdmin>>.Ok(liste);
    }

    /// <summary>Crée un rôle.</summary>
    public async Task<Resultat<bool>> CreerRoleAsync(
        string nom, string? description, IReadOnlyList<string> permissions,
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/identity/roles",
            new { name = nom, description, permissions }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Renomme un rôle et change sa description.</summary>
    /// <remarks>
    /// LE NOM EST OBLIGATOIRE, LA DESCRIPTION NON.
    ///
    /// `UpdateRoleRequest(string Name, string? Description)` : envoyer un nom vide
    /// produit un 400 de validation, pas une conservation de l'ancien. L'écran
    /// refuse donc d'enregistrer un nom vide plutôt que de le laisser partir.
    /// </remarks>
    public async Task<Resultat<bool>> RenommerRoleAsync(
        Guid role, string nom, string? description, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Put, $"/api/identity/roles/{role}",
            new { name = nom, description }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Remplace l'ENSEMBLE des permissions d'un rôle.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST UN REMPLACEMENT, PAS UN AJOUT.
    ///
    /// `SetRolePermissionsCommand` pose la liste reçue à la place de l'ancienne :
    /// une permission absente de l'envoi est RETIRÉE. Un écran qui n'enverrait
    /// que les lignes ajoutées effacerait toutes les autres, sans le dire.
    ///
    /// L'écran envoie donc toujours la liste complète, telle qu'elle est affichée
    /// — et c'est pourquoi il la charge avant de la laisser éditer.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> PoserPermissionsAsync(
        Guid role, IReadOnlyList<string> permissions, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Put, $"/api/identity/roles/{role}/permissions",
            new { permissions }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Supprime un rôle non-système.</summary>
    public async Task<Resultat<bool>> SupprimerRoleAsync(Guid role, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Delete, $"/api/identity/roles/{role}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES MARQUES (§10, §16).
    //
    // LA LECTURE EST SUR LE GROUPE PUBLIC, LES ÉCRITURES SUR LE GROUPE ADMIN.
    //
    // `GET /api/v1/catalog/brands` est `AllowAnonymous` — c'est la même route
    // que consulte la vitrine — tandis que la création, la modification, la
    // publication, la suppression et la file des demandes vivent sous
    // `/api/v1/catalog/admin`. Rien à ouvrir : seulement à ne pas chercher la
    // liste là où sont les gestes.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Le référentiel complet des marques.</summary>
    /// <remarks>
    /// AUCUNE PAGINATION, AUCUN FILTRE, ET C'EST ASSUMÉ CÔTÉ SERVEUR.
    ///
    /// `ListBrandsQuery` n'a pas de paramètre : le service rend tout, depuis un
    /// cache de donnée de référence invalidé à chaque écriture sur une marque
    /// (`CatalogDbContext` ajoute `catalog:brands:all` aux clés à purger). La
    /// recherche de l'écran est donc locale — et elle ne peut pas rater une
    /// marque, puisque tout est là.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<MarqueAdmin>>> ListerMarquesAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/v1/catalog/brands", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<MarqueAdmin>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<MarqueAdmin>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<MarqueAdmin>>.Echec("Référentiel des marques illisible.")
            : Resultat<IReadOnlyList<MarqueAdmin>>.Ok(enveloppe.Data);
    }

    /// <summary>La file des demandes de marque en attente.</summary>
    public async Task<Resultat<IReadOnlyList<DemandeMarqueAdmin>>> ListerDemandesMarqueAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/v1/catalog/admin/brands/requests", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<DemandeMarqueAdmin>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<DemandeMarqueAdmin>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<DemandeMarqueAdmin>>.Echec("File des demandes illisible.")
            : Resultat<IReadOnlyList<DemandeMarqueAdmin>>.Ok(enveloppe.Data);
    }

    /// <summary>Crée une marque directement, sans demande préalable.</summary>
    /// <remarks>
    /// LA MARQUE NAÎT EN `Pending`, PAS EN `Active`.
    ///
    /// `Brand.Create` pose `Status = BrandStatus.Pending` ; seule la publication
    /// la rend active. Un écran qui annoncerait « marque créée » sans plus
    /// laisserait croire qu'elle est utilisable — elle ne le sera qu'après
    /// `publish`.
    /// </remarks>
    public async Task<Resultat<bool>> CreerMarqueAsync(
        string nom, string? logo, string? description, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/v1/catalog/admin/brands",
            new { name = nom, logoUrl = logo, description }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Modifie le nom, le logo et la description d'une marque.</summary>
    /// <remarks>
    /// LE CORPS EST COMPLET, PAS PARTIEL — C'EST UN `PUT`.
    ///
    /// `BrandRequest(string Name, string? LogoUrl, string? Description)` :
    /// `UpdateBrandCommand` reçoit les trois champs et les pose. Omettre le logo
    /// dans l'envoi ne le conserve pas, il l'EFFACE. L'écran renvoie donc
    /// toujours les trois valeurs telles qu'elles sont affichées.
    ///
    /// LE SLUG, LUI, NE SUIT PAS LE NOM. Il est calculé à la création et sert de
    /// clé d'unicité ; renommer « Samsng » en « Samsung » laisse le slug
    /// « samsng ». Ce n'est pas visible à l'écran de la vitrine, mais cela le
    /// devient dans une URL.
    /// </remarks>
    public async Task<Resultat<bool>> ModifierMarqueAsync(
        Guid marque, string nom, string? logo, string? description, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Put, $"/api/v1/catalog/admin/brands/{marque}",
            new { name = nom, logoUrl = logo, description }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Publie ou dépublie une marque.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « DÉPUBLIER » RAMÈNE À `Pending`, PAS À `Archived`.
    ///
    /// `Brand.Unpublish()` pose `Status = BrandStatus.Pending` : la marque
    /// retourne en attente de décision, elle n'est pas retirée du référentiel.
    /// `Brand.Archive()` existe dans le domaine et fait cela — mais AUCUN
    /// endpoint ne l'appelle. Il n'y a donc aujourd'hui aucun moyen d'archiver
    /// une marque autrement qu'en la supprimant, ce qui n'est pas la même chose.
    ///
    /// Les deux gestes refusent d'agir sur une marque déjà archivée : le domaine
    /// renvoie un échec, et le message remonte tel quel à l'écran.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> PublierMarqueAsync(
        Guid marque, bool publier, CancellationToken jeton = default)
    {
        var geste = publier ? "publish" : "unpublish";

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/brands/{marque}/{geste}",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Supprime définitivement une marque du référentiel.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SUPPRESSION RÉELLE, ET LE SERVEUR NE VÉRIFIE PAS SI DES PRODUITS S'EN
    ///    SERVENT.
    ///
    /// `DeleteBrandCommandHandler` charge la marque et appelle `Remove` : pas de
    /// comptage de fiches, pas de refus. Et `ProductRevisionConfiguration`
    /// déclare `BrandId` comme une simple propriété indexée, SANS clé étrangère
    /// vers `brands` — la base ne s'y opposera donc pas davantage.
    ///
    /// Les révisions de produit qui portaient cet identifiant continuent de le
    /// porter, vers une ligne qui n'existe plus. Rien ne casse bruyamment : le
    /// filtre par marque de la vitrine cesse simplement de les trouver.
    ///
    /// CE QUE CET ÉCRAN FAIT : il exige une ré-authentification et un motif écrit
    /// avant d'envoyer, comme pour tout geste destructeur.
    /// CE QU'IL NE FAIT PAS : vérifier qu'aucun produit ne référence la marque —
    /// il n'existe aucune route pour le demander. La prudence reste humaine, et
    /// c'est un manque côté serveur, pas une précaution à écrire ici.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> SupprimerMarqueAsync(Guid marque, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Delete, $"/api/v1/catalog/admin/brands/{marque}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Approuve une demande : crée la marque, ou rattache à une marque existante.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE RATTACHEMENT EST LE CAS FRÉQUENT, PAS L'EXCEPTION.
    ///
    /// Le commentaire d'`ApproveBrandRequestCommand` le dit : « Une demande
    /// "samsumg" se rattache au "Samsung" déjà au catalogue. Ne permettre que la
    /// création ferait de ce mécanisme la source du problème qu'il devait
    /// résoudre. » L'écran offre donc les deux gestes côte à côte, et le
    /// rattachement se lit sur la marque sélectionnée dans le référentiel.
    ///
    /// SI LE SLUG EST DÉJÀ PRIS, LA CRÉATION ÉCHOUE EN 409 AVEC UNE CONSIGNE.
    ///
    /// Le serveur répond « Rattachez la demande à la marque existante au lieu
    /// d'en créer une seconde ». Ce message est affiché tel quel : c'est
    /// exactement la marche à suivre, et le reformuler la rendrait plus vague.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <param name="marqueExistante">
    /// <c>null</c> pour créer une marque au nom demandé ; sinon la marque à
    /// laquelle la demande est rattachée.
    /// </param>
    public async Task<Resultat<Guid>> ApprouverDemandeMarqueAsync(
        Guid demande, Guid? marqueExistante, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/brands/requests/{demande}/approve",
            new { existingBrandId = marqueExistante }, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<Guid>.Echec(reponse.Message!);
        }

        // L'identifiant rendu sert à sélectionner la marque concernée après
        // rechargement. S'il est illisible, le geste a tout de même abouti : on
        // ne transforme pas une réponse mal lue en échec d'approbation.
        var enveloppe = Lire<EnveloppeApi<ReponseApprobation>>(reponse.Valeur);

        return Resultat<Guid>.Ok(enveloppe?.Data?.BrandId ?? Guid.Empty);
    }

    /// <summary>Refuse une demande de marque, avec motif.</summary>
    /// <remarks>
    /// LE MOTIF PART VERS LE VENDEUR, ET C'EST TOUT CE QU'IL RECEVRA.
    ///
    /// `RejectBrandRequestBody(string Reason)` : un seul champ, obligatoire. Le
    /// vendeur ne verra pas d'autre explication, et une demande refusée peut
    /// repartir corrigée — le filtre `GetPendingByNameAsync` ne bloque que les
    /// demandes ENCORE en attente. Un motif vague produit donc la même demande
    /// une seconde fois.
    /// </remarks>
    public async Task<Resultat<bool>> RefuserDemandeMarqueAsync(
        Guid demande, string motif, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/brands/requests/{demande}/reject",
            new { reason = motif }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES CATÉGORIES ET LES ATTRIBUTS (§10, §13, §23).
    //
    // MÊME RÉPARTITION QUE LES MARQUES : LECTURE PUBLIQUE, ÉCRITURES ADMIN.
    //
    // `GET /categories`, `GET /categories/{id}/attributes` sont `AllowAnonymous`
    // sur le groupe public — c'est ce que consomme le formulaire vendeur. Les
    // écritures et le référentiel des définitions sont sur `/admin`.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>L'arbre complet des catégories, à plat.</summary>
    /// <remarks>
    /// LE SERVICE REND UNE LISTE, PAS UN ARBRE : LA HIÉRARCHIE EST DANS `path`.
    ///
    /// `ListCategoriesQuery` n'a aucun paramètre et sert le cache le plus rentable
    /// du système — « quelques dizaines de lignes, modifiées quelques fois par an,
    /// relues à chaque ouverture de l'application ». L'écran reconstruit donc
    /// l'arbre localement, en triant sur le chemin.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<CategorieAdmin>>> ListerCategoriesAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/v1/catalog/categories", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<CategorieAdmin>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<CategorieAdmin>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<CategorieAdmin>>.Echec("Arbre des catégories illisible.")
            : Resultat<IReadOnlyList<CategorieAdmin>>.Ok(enveloppe.Data);
    }

    /// <summary>Crée une catégorie, éventuellement sous un parent.</summary>
    /// <remarks>
    /// LE PARENT SE CHOISIT À LA CRÉATION, ET SEULEMENT LÀ.
    ///
    /// `CategoryRequest` porte `ParentId`, mais `UpdateCategoryCommand` ne le
    /// reprend PAS : il n'existe aucune route pour déplacer une catégorie d'une
    /// branche à une autre. Une catégorie créée au mauvais endroit se supprime et
    /// se recrée — ce qui, vu ce que fait la suppression, n'est pas anodin.
    /// </remarks>
    public async Task<Resultat<bool>> CreerCategorieAsync(
        string nom, Guid? parent, CancellationToken jeton = default)
    {
        // Schéma d'attributs vide et non nul : la colonne est `NOT NULL` en base,
        // et le domaine pose `"{}"` par défaut. Envoyer `null` fonctionnerait par
        // ce défaut ; l'écrire ici dit ce que la catégorie porte réellement.
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/v1/catalog/admin/categories",
            new { name = nom, parentId = parent, imageUrl = (string?)null, attributeSchema = "{}" },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Modifie le nom, l'image et le schéma d'attributs d'une catégorie.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// RENOMMER RECALCULE LE SLUG ET LE CHEMIN — MAIS PAS CEUX DES DESCENDANTS.
    ///
    /// `Category.Update` refait `Slug` puis `Path = BuildPath(parentPath, slug)`.
    /// Les enfants, eux, ne sont pas touchés : leur `Path` conserve l'ANCIEN
    /// segment. Or `ListDescendantsAsync` cherche `Path.StartsWith(chemin + "/")`.
    ///
    /// CONSÉQUENCE : après un renommage, la branche est coupée. Une publication en
    /// cascade sur le parent renommé rend `affected = 1` et ne touche plus aucun
    /// enfant, sans erreur ni message. Rien ne le signale côté serveur.
    ///
    /// L'écran compare les chemins et affiche un bandeau quand il trouve un enfant
    /// dont le chemin ne descend plus de son parent déclaré. CE QU'IL NE FAIT PAS :
    /// réparer — aucune route ne permet de réécrire le chemin d'un descendant.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> ModifierCategorieAsync(
        Guid categorie, string nom, string? image, string schema, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Put, $"/api/v1/catalog/admin/categories/{categorie}",
            new { name = nom, imageUrl = image, attributeSchema = schema },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Publie ou dépublie une catégorie, avec ou sans sa descendance.</summary>
    /// <remarks>
    /// LE CORPS EST OBLIGATOIRE, MÊME POUR NE RIEN PROPAGER.
    ///
    /// `CascadeRequest(bool IncludeDescendants = false)` est un paramètre de corps
    /// non nullable : un `POST` sans corps donne un 400 de liaison, et non le
    /// comportement par défaut que la valeur du record laisse espérer.
    /// </remarks>
    public async Task<Resultat<int>> PublierCategorieAsync(
        Guid categorie, bool publier, bool cascade, CancellationToken jeton = default)
    {
        var geste = publier ? "publish" : "unpublish";

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/categories/{categorie}/{geste}",
            new { includeDescendants = cascade }, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<int>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<ReponseCascade>>(reponse.Valeur);

        // Le compte est une information, pas une condition de succès : illisible,
        // on rend 0 plutôt que de transformer une bascule réussie en échec.
        return Resultat<int>.Ok(enveloppe?.Data?.Affected ?? 0);
    }

    /// <summary>Supprime définitivement une catégorie.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SUPPRESSION RÉELLE, SANS AUCUN CONTRÔLE DE DESCENDANCE NI DE CONTENU.
    ///
    /// `DeleteCategoryCommandHandler` charge et appelle `Remove` : ni comptage
    /// d'enfants, ni comptage de fiches. Et `CategoryConfiguration` déclare
    /// `ParentId` en simple propriété indexée, SANS clé étrangère — la base ne
    /// s'y oppose pas davantage.
    ///
    /// Supprimer un nœud intermédiaire laisse donc toute sa branche en place, avec
    /// des `ParentId` qui ne désignent plus rien et des chemins dont le préfixe
    /// n'existe plus. L'arbre reste affichable — il devient faux.
    ///
    /// CE QUE FAIT L'ÉCRAN : il refuse d'envoyer quand la catégorie a des enfants
    /// connus, et exige motif plus ré-authentification pour une feuille.
    /// CE QU'IL NE PEUT PAS FAIRE : savoir si des produits y sont rattachés —
    /// aucune route ne permet de le demander.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> SupprimerCategorieAsync(
        Guid categorie, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Delete, $"/api/v1/catalog/admin/categories/{categorie}",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Toutes les définitions d'attributs connues.</summary>
    public async Task<Resultat<IReadOnlyList<DefinitionAttribut>>> ListerDefinitionsAttributsAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/v1/catalog/admin/attributes", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<DefinitionAttribut>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<DefinitionAttribut>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<DefinitionAttribut>>.Echec("Référentiel d'attributs illisible.")
            : Resultat<IReadOnlyList<DefinitionAttribut>>.Ok(enveloppe.Data);
    }

    /// <summary>Crée une définition d'attribut réutilisable.</summary>
    /// <remarks>
    /// LES NEUF TYPES SONT UNE LISTE FERMÉE, RECOPIÉE DU MESSAGE D'ERREUR SERVEUR.
    ///
    /// `Enum.TryParse&lt;AttributeValueType&gt;` après suppression des soulignés :
    /// TEXT, TEXTAREA, INTEGER, DECIMAL, BOOLEAN, SELECT, MULTI_SELECT, COLOR,
    /// DATE. Contrairement aux permissions de rôle, la liste fermée EXISTE — une
    /// liste déroulante est donc ici le bon outil, et non un pari.
    /// </remarks>
    public async Task<Resultat<bool>> CreerDefinitionAttributAsync(
        string code, string nom, string type, string? unite, IReadOnlyList<string> options,
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/v1/catalog/admin/attributes",
            new { code, name = nom, type, unit = unite, options }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Les attributs exigés par une catégorie.</summary>
    public async Task<Resultat<IReadOnlyList<AttributCategorie>>> ListerAttributsDeCategorieAsync(
        Guid categorie, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/v1/catalog/categories/{categorie}/attributes",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<AttributCategorie>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<AttributCategorie>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<AttributCategorie>>.Echec("Schéma de la catégorie illisible.")
            : Resultat<IReadOnlyList<AttributCategorie>>.Ok(enveloppe.Data);
    }

    /// <summary>Rattache un attribut à une catégorie, ou en met à jour les réglages.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « REQUIS » A UN EFFET IMMÉDIAT SUR LES VENDEURS, PAS SUR L'AFFICHAGE SEUL.
    ///
    /// `ChangeProductStatusCommandHandler` appelle `ValidationDesAttributs.Valider`
    /// au passage en `PendingReview` : dès ce rattachement enregistré, toute
    /// nouvelle soumission dans cette catégorie sans cet attribut est REFUSÉE.
    ///
    /// CE QUE CELA NE FAIT PAS : invalider les fiches déjà publiées. La règle ne
    /// s'applique qu'à la soumission — les brouillons en cours, eux, la
    /// rencontreront au moment de partir en validation, sans avertissement
    /// préalable.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> RattacherAttributAsync(
        Guid categorie, Guid attribut, bool requis, bool variante, int ordre,
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/catalog/admin/categories/{categorie}/attributes",
            new { attributeDefinitionId = attribut, required = requis, variant = variante, displayOrder = ordre },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Retire un attribut du schéma d'une catégorie.</summary>
    /// <remarks>
    /// RÉVERSIBLE : LES FICHES EXISTANTES GARDENT LEURS VALEURS.
    ///
    /// Elles vivent dans `product_revisions.attributes`, pas dans le rattachement.
    /// Retirer l'attribut cesse de l'EXIGER et de l'afficher ; il ne l'efface
    /// nulle part. Le retrait est idempotent côté serveur.
    /// </remarks>
    public async Task<Resultat<bool>> DetacherAttributAsync(
        Guid categorie, Guid attribut, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Delete, $"/api/v1/catalog/admin/categories/{categorie}/attributes/{attribut}",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA TARIFICATION DES COURSES (delivery-pricing-service).
    //
    // CE GROUPE A ÉTÉ OUVERT À TOUT LE MONDE PENDANT UN TEMPS, ET LE SERVICE LE
    //    DIT LUI-MÊME.
    //
    // Son commentaire : « `MapGroup` nu, dans un hôte qui n'appelait ni
    // `UseAuthentication` ni `UseAuthorization` : n'importe qui pouvait créer,
    // modifier et activer une règle de tarification ». C'est fermé — deux
    // verrous, `AddHbaSecurity` et `MapAdminGroup`. On le rappelle ici parce que
    // c'est le groupe le plus lourd de conséquences de toute la console : il
    // décide de ce que la plateforme facture et reverse.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Toutes les règles tarifaires, priorité décroissante.</summary>
    /// <remarks>
    /// L'ORDRE VIENT DU SERVEUR ET IL EST SIGNIFIANT : `ListRulesAsync` trie par
    /// `Priority` décroissante, c'est-à-dire dans l'ordre où le moteur de devis
    /// les regarde. L'écran ne le retrie pas.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<RegleTarifaire>>> ListerReglesTarifairesAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/v1/admin/delivery-pricing/rules", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<RegleTarifaire>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<RegleTarifaire>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<RegleTarifaire>>.Echec("Grille tarifaire illisible.")
            : Resultat<IReadOnlyList<RegleTarifaire>>.Ok(enveloppe.Data);
    }

    /// <summary>Crée ou remplace une règle tarifaire.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE `PATCH` EST UN REMPLACEMENT COMPLET, MALGRÉ SON VERBE.
    ///
    /// `UpdateRuleAsync` reconstruit l'enregistrement à partir de la requête :
    /// seuls `ServiceLevel`, `VehicleType` et `SurgeMultiplier` retombent sur
    /// l'ancienne valeur quand ils sont nuls. Tout le reste est écrasé par ce
    /// qu'on envoie — y compris `MaxFee`, dont l'absence RETIRE le plafond, et
    /// `ActiveFrom`, qui n'est pas nullable : un corps sans ce champ vaudrait
    /// `0001-01-01`, accepté sans broncher.
    ///
    /// L'écran envoie donc toujours les douze champs, tels qu'ils sont affichés.
    ///
    /// `Status` N'EST PAS DANS LA REQUÊTE : il ne se change que par
    /// `activate` / `deactivate`. Une règle modifiée reste dans l'état où elle
    /// était.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> EnregistrerRegleTarifaireAsync(
        Guid? regle,
        string nom, string portee, string niveau, string? vehicule,
        long baseFee, long parKm, long parMinute, long minimum, long? maximum,
        DateTimeOffset debut, DateTimeOffset? fin, int priorite, decimal multiplicateur,
        CancellationToken jeton = default)
    {
        var corps = new
        {
            name = nom,
            scope = portee,
            baseFee,
            perKmFee = parKm,
            perMinuteFee = parMinute,
            minFee = minimum,
            maxFee = maximum,
            activeFrom = debut,
            activeTo = fin,
            priority = priorite,
            serviceLevel = niveau,
            vehicleType = vehicule,
            surgeMultiplier = multiplicateur,
        };

        var reponse = regle is { } identifiant
            ? await EnvoyerAsync(
                HttpMethod.Patch, $"/api/v1/admin/delivery-pricing/rules/{identifiant}",
                corps, authentifier: true, jeton)
            : await EnvoyerAsync(
                HttpMethod.Post, "/api/v1/admin/delivery-pricing/rules",
                corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Active ou désactive une règle tarifaire.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DÉSACTIVER LA DERNIÈRE RÈGLE ÉLIGIBLE CASSE TOUS LES DEVIS DE COURSE.
    ///
    /// `CreateQuoteAsync` termine sa requête par `FirstAsync` — et non
    /// `FirstOrDefaultAsync`. Sans règle éligible, la méthode LÈVE, et chaque
    /// demande de devis répond 500 : plus aucune commande marketplace ni repas ne
    /// peut être passée, puisque le devis se relit chez delivery-pricing avant la
    /// création de la course.
    ///
    /// Le semis de secours ne rattrape pas : `EnsureSeedAsync` n'insère la règle
    /// « Cotonou standard » que si la table est VIDE. Des règles présentes et
    /// toutes inactives ne déclenchent rien.
    ///
    /// L'ÉCRAN REFUSE DONC CETTE DÉSACTIVATION-LÀ. C'est une garde du client :
    /// le serveur, lui, l'accepte sans rien dire.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> BasculerRegleTarifaireAsync(
        Guid regle, bool activer, CancellationToken jeton = default)
    {
        var geste = activer ? "activate" : "deactivate";

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/admin/delivery-pricing/rules/{regle}/{geste}",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES LOTS DE REVERSEMENT (§ règlement).
    //
    // LECTURE SEULE, ET CE N'EST PAS UN OUBLI DE LA PASSERELLE.
    //
    // La route `settlements` d'`api-gateway` déclare
    // `Methods: [GET, HEAD, OPTIONS]`, avec ce motif écrit dans ses métadonnées :
    // « Le lancement d'un règlement vit sous /api/financial/settlements dans un
    // groupe MapAdminGroup voisin ; une route sans restriction de méthode
    // l'exposerait au proxy. Le service refuserait, mais on ne compte pas
    // là-dessus : la passerelle ne doit pas relayer ce qu'elle n'a pas
    // l'intention d'ouvrir. »
    //
    // Les quatre gestes — lancer un lot, l'annuler, marquer un versement payé ou
    // échoué — ne sont donc atteignables que depuis le réseau interne. Ils
    // déplacent de l'argent, et l'un d'eux est irréversible : déclarer un
    // versement payé débite le vendeur, et le déclarer ensuite échoué est refusé
    // puisque, du point de vue du système, l'argent est parti.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Les lots de reversement, chacun avec ses versements.</summary>
    public async Task<Resultat<IReadOnlyList<LotReglement>>> ListerLotsReglementAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/financial/settlements", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<LotReglement>>.Echec(reponse.Message!);
        }

        // `Match(Results.Ok)` ET NON `ApiResults.Ok` : LE CORPS EST NU.
        //
        // Les routes de règlement rendent la valeur directement, sans enveloppe
        // `{data}`. La lire comme une `EnveloppeApi` donnerait `Data` nul sans
        // erreur, et la liste s'afficherait vide.
        var liste = Lire<List<LotReglement>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<LotReglement>>.Echec("Liste des lots illisible.")
            : Resultat<IReadOnlyList<LotReglement>>.Ok(liste);
    }

    /// <summary>Le relevé d'un vendeur sur une période.</summary>
    /// <remarks>
    /// LES DEUX DATES SONT OBLIGATOIRES, ET NON NULLABLES CÔTÉ SERVEUR.
    ///
    /// `GetSellerStatementAsync(Guid sellerId, DateTime periodStartUtc,
    /// DateTime periodEndUtc, …)` : omettre un paramètre donne un 400 de liaison.
    /// L'écran passe la période du lot, au format aller-retour ISO.
    ///
    /// LA GARDE D'APPARTENANCE LAISSE PASSER L'ADMINISTRATEUR.
    /// `DenyUnlessOwnSellerAsync` rend `null` — donc autorise — quand l'appelant
    /// porte le rôle `Admin` ou `Moderator`, avant même de chercher son dossier
    /// vendeur.
    /// </remarks>
    public async Task<Resultat<ReleveVendeur>> LireReleveVendeurAsync(
        Guid vendeur, DateTime debutUtc, DateTime finUtc, CancellationToken jeton = default)
    {
        var parametres =
            $"periodStartUtc={Uri.EscapeDataString(debutUtc.ToString("O"))}"
            + $"&periodEndUtc={Uri.EscapeDataString(finUtc.ToString("O"))}";

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/financial/settlements/sellers/{vendeur}/statement?{parametres}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<ReleveVendeur>.Echec(reponse.Message!);
        }

        var releve = Lire<ReleveVendeur>(reponse.Valeur);

        return releve is null
            ? Resultat<ReleveVendeur>.Echec("Relevé vendeur illisible.")
            : Resultat<ReleveVendeur>.Ok(releve);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES PORTEFEUILLES (wallet-service, relayé par /api/wallet).
    //
    // `take` EST OBLIGATOIRE, ET NON NULLABLE CÔTÉ SERVEUR.
    //
    // `ListPlatformWalletTransactionsAsync(int take, …)` déclare un `int` nu :
    // la valeur par défaut de `ListPlatformWalletTransactionsQuery(int Take = 50)`
    // n'est JAMAIS atteinte depuis HTTP, parce que la liaison échoue avant, sur
    // un paramètre requis absent. L'écran envoie donc toujours le nombre.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Les quatre soldes du portefeuille de la plateforme.</summary>
    public async Task<Resultat<PortefeuillePlateforme>> LirePortefeuillePlateformeAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/wallet/platform", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PortefeuillePlateforme>.Echec(reponse.Message!);
        }

        var portefeuille = Lire<PortefeuillePlateforme>(reponse.Valeur);

        return portefeuille is null
            ? Resultat<PortefeuillePlateforme>.Echec("Portefeuille plateforme illisible.")
            : Resultat<PortefeuillePlateforme>.Ok(portefeuille);
    }

    /// <summary>Le grand livre de la plateforme, les <paramref name="combien"/> dernières écritures.</summary>
    public async Task<Resultat<IReadOnlyList<EcritureWallet>>> ListerEcrituresPlateformeAsync(
        int combien, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/wallet/platform/transactions?take={combien}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<EcritureWallet>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<EcritureWallet>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<EcritureWallet>>.Echec("Grand livre illisible.")
            : Resultat<IReadOnlyList<EcritureWallet>>.Ok(liste);
    }

    /// <summary>Le portefeuille d'un vendeur.</summary>
    /// <remarks>
    /// L'ADMINISTRATEUR PASSE LA GARDE D'APPARTENANCE, ET C'EST TOUT CE QUI LUI
    ///    OUVRE CETTE ROUTE.
    ///
    /// `DenyUnlessOwnSellerAsync` rend `null` pour un porteur du rôle `Admin` ou
    /// `Moderator` avant de chercher un dossier vendeur. Sans ce court-circuit,
    /// la console recevrait un 403 sur chaque consultation — l'administrateur
    /// n'étant, lui, le propriétaire d'aucune boutique.
    /// </remarks>
    public async Task<Resultat<PortefeuilleVendeur>> LirePortefeuilleVendeurAsync(
        Guid vendeur, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/wallet/sellers/{vendeur}", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PortefeuilleVendeur>.Echec(reponse.Message!);
        }

        var portefeuille = Lire<PortefeuilleVendeur>(reponse.Valeur);

        return portefeuille is null
            ? Resultat<PortefeuilleVendeur>.Echec("Portefeuille vendeur illisible.")
            : Resultat<PortefeuilleVendeur>.Ok(portefeuille);
    }

    /// <summary>Le portefeuille d'un livreur.</summary>
    public async Task<Resultat<PortefeuilleLivreur>> LirePortefeuilleLivreurAsync(
        Guid livreur, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/wallet/drivers/{livreur}", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PortefeuilleLivreur>.Echec(reponse.Message!);
        }

        var portefeuille = Lire<PortefeuilleLivreur>(reponse.Valeur);

        return portefeuille is null
            ? Resultat<PortefeuilleLivreur>.Echec("Portefeuille livreur illisible.")
            : Resultat<PortefeuilleLivreur>.Ok(portefeuille);
    }

    /// <summary>Les écritures d'un vendeur ou d'un livreur.</summary>
    /// <remarks>
    /// LES DEUX ROUTES SONT SYMÉTRIQUES, LE CHEMIN SEUL CHANGE.
    ///
    /// `/sellers/{id}/transactions` et `/drivers/{id}/transactions` rendent la
    /// même `WalletTransactionView` et prennent le même `take` obligatoire.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<EcritureWallet>>> ListerEcrituresDeCompteAsync(
        bool vendeur, Guid identifiant, int combien, CancellationToken jeton = default)
    {
        var segment = vendeur ? "sellers" : "drivers";

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/wallet/{segment}/{identifiant}/transactions?take={combien}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<EcritureWallet>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<EcritureWallet>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<EcritureWallet>>.Echec("Écritures illisibles.")
            : Resultat<IReadOnlyList<EcritureWallet>>.Ok(liste);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LE STOCK (inventory-service).
    //
    // `take` EST NULLABLE ICI, CONTRAIREMENT AUX ROUTES DE PORTEFEUILLE.
    //
    // `LowStockAsync(int? take, …)` accepte l'absence et retombe sur 50. Les
    // routes wallet déclarent un `int` nu et refusent le corps sans paramètre.
    // Deux services, deux conventions : c'est l'endpoint qui tranche, pas
    // l'habitude.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Les articles sous leur seuil de réapprovisionnement.</summary>
    /// <remarks>
    /// C'EST UNE ALERTE, PAS UN INVENTAIRE — ET LE SERVEUR L'IMPOSE.
    ///
    /// `ListLowStockQueryHandler` plafonne à 200 quoi qu'on demande :
    /// « deux cents lignes sous seuil, c'est déjà plus que ce qu'un gestionnaire
    /// traite dans sa journée. Le plafond est posé ICI, dans l'application, et non
    /// laissé au client. » Demander davantage ne rend pas davantage, et l'écran ne
    /// propose donc pas de valeur au-delà.
    /// </remarks>
    public async Task<Resultat<IReadOnlyList<ArticleStock>>> ListerStockBasAsync(
        int combien, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/inventory/low-stock?take={combien}", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<ArticleStock>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<ArticleStock>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<ArticleStock>>.Echec("Alertes de stock illisibles.")
            : Resultat<IReadOnlyList<ArticleStock>>.Ok(liste);
    }

    /// <summary>Tous les lieux d'expédition de la plateforme.</summary>
    public async Task<Resultat<IReadOnlyList<LieuStock>>> ListerLieuxStockAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/inventory/locations", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<LieuStock>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<LieuStock>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<LieuStock>>.Echec("Liste des lieux illisible.")
            : Resultat<IReadOnlyList<LieuStock>>.Ok(liste);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES COMPTES (identity-service).
    //
    // LA ROUTE DE LISTE VIENT D'ÊTRE OUVERTE, ET LA REQUÊTE L'ATTENDAIT.
    //
    // `ListUsersQuery` existait entièrement écrite — recherche, statut, tri,
    // pagination, comptage par statut — sans qu'aucune route ne la monte. Les
    // cinq gestes d'administration étant tous adressés par GUID, aucune console
    // ne pouvait exister avant elle.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Une page de comptes.</summary>
    /// <remarks>
    /// LA RECHERCHE NE PORTE QUE SUR LE PRÉNOM ET LE NOM.
    ///
    /// `UserRepository.ListPagedAsync` : « ILike uniquement sur des colonnes
    /// string simples : Email/PhoneNumber sont des value objects convertis, non
    /// traduisibles. » Chercher un compte par son e-mail — le geste de support le
    /// plus courant — ne rend donc RIEN, sans erreur. L'écran le dit.
    ///
    /// Les tris acceptés sont `name`, `status`, et par défaut la date de création.
    /// Une autre valeur retombe silencieusement sur la date.
    /// </remarks>
    public async Task<Resultat<PageApi<CompteAdmin>>> ListerComptesAsync(
        int page, int taille, string? recherche, string? statut, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(recherche))
        {
            parametres.Add($"search={Uri.EscapeDataString(recherche.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/identity/users?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<CompteAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<CompteAdmin>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<CompteAdmin>>.Echec("Liste des comptes illisible.")
            : Resultat<PageApi<CompteAdmin>>.Ok(resultat);
    }

    /// <summary>Suspend ou réactive un compte.</summary>
    /// <remarks>
    /// AUCUN DES DEUX GESTES NE SORT DE L'ÉTAT `Deleted`.
    ///
    /// Un compte supprimé à la demande de son titulaire est ANONYMISÉ : nom,
    /// e-mail et téléphone remplacés, mot de passe détruit. Le domaine ne fournit
    /// aucune méthode pour en revenir — « les données d'origine n'existent plus,
    /// il n'y a rien à restaurer ». L'écran grise donc les deux boutons sur ces
    /// comptes plutôt que d'envoyer un geste qui ne fera rien.
    /// </remarks>
    public async Task<Resultat<bool>> BasculerCompteAsync(
        Guid compte, bool suspendre, CancellationToken jeton = default)
    {
        var geste = suspendre ? "suspend" : "reactivate";

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/identity/users/{compte}/{geste}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Assigne un rôle à un compte.</summary>
    public async Task<Resultat<bool>> AssignerRoleAsync(
        Guid compte, Guid role, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/identity/users/{compte}/roles",
            new { roleId = role }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Retire un rôle d'un compte.</summary>
    public async Task<Resultat<bool>> RetirerRoleAsync(
        Guid compte, Guid role, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Delete, $"/api/identity/users/{compte}/roles/{role}",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES DOSSIERS DE RETOUR (§ retours et remboursements).
    //
    // LA LISTE VIENT D'ÊTRE OUVERTE ; LES TROIS AUTRES ROUTES ÉTAIENT LÀ.
    //
    // `GET /{id}`, `override` et `close` existaient, toutes adressées par GUID :
    // aucun écran ne pouvait exister avant qu'une route dise QUELS dossiers
    // attendent.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Une page de dossiers de retour, filtrable par statut.</summary>
    /// <remarks>
    /// LE FILTRE S'ENVOIE EN NOM, LA RÉPONSE REVIENT EN NUMÉRO.
    ///
    /// La route lit `status` avec `Enum.TryParse` — donc « ManualReview » — mais
    /// `ReturnRequestDto` porte l'énumération elle-même, sérialisée en entier
    /// faute de `JsonStringEnumConverter`. Ce n'est pas une erreur de lecture :
    /// c'est l'API qui n'est pas symétrique.
    /// </remarks>
    public async Task<Resultat<PageApi<DossierRetour>>> ListerRetoursAsync(
        int page, int taille, string? statut, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/v1/admin/returns?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<DossierRetour>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<DossierRetour>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<DossierRetour>>.Echec("Liste des dossiers illisible.")
            : Resultat<PageApi<DossierRetour>>.Ok(resultat);
    }

    /// <summary>Rejette un dossier de retour, avec motif.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LA ROUTE S'APPELLE `override`, LA COMMANDE S'APPELLE `RejectReturnCommand`.
    ///
    /// `OverrideAsync` envoie `new RejectReturnCommand(id, reason, reviewer)` :
    /// ce geste REJETTE le dossier. « Arbitrer » ou « passer outre » laisserait
    /// croire à une décision neutre, ou à un déblocage. L'écran écrit donc
    /// « Rejeter le dossier », qui est ce qui se passe.
    ///
    /// Le motif est obligatoire côté serveur : un corps sans motif rend un 400
    /// explicite avant même d'atteindre la commande.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> RejeterRetourAsync(
        Guid dossier, string motif, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/admin/returns/{dossier}/override",
            new { reason = motif }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Clôt un dossier de retour.</summary>
    public async Task<Resultat<bool>> CloreRetourAsync(Guid dossier, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/admin/returns/{dossier}/close",
            null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LA MODÉRATION DES AVIS (review-service, relayé par /api/reviews).
    //
    // LA FILE VIENT D'ÊTRE OUVERTE ; LES TROIS GESTES ÉTAIENT LÀ DEPUIS LE DÉBUT.
    //
    // `flag`, `reject` et `restore` sont montés sur le groupe d'administration,
    // adressés par identifiant d'avis. Rien ne disait QUELS avis attendent :
    // `ListByProductAsync` ne rend que le publié, `ListBySellerAsync` demande un
    // vendeur. Un avis signalé restait `Flagged` sans que personne ne le voie.
    //
    // LE CHEMIN EST `/moderation`, PAS `/`.
    //
    // Le groupe admin partage son préfixe avec le groupe authentifié, qui monte
    // déjà `GET /{id}` et `POST /`. Une seconde route sur `/` aurait été arbitrée
    // par l'ordre d'enregistrement, sans erreur pour le signaler.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>La file de modération des avis, filtrable par statut.</summary>
    /// <remarks>
    /// SANS FILTRE, ELLE REND TOUT — Y COMPRIS LE PUBLIÉ.
    ///
    /// Restreindre la route aux seuls signalés interdirait de relire un avis
    /// rejeté pour le restaurer, ce que `restore` permet précisément.
    /// </remarks>
    public async Task<Resultat<PageApi<AvisAdmin>>> ListerAvisAsync(
        int page, int taille, string? statut, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/reviews/moderation?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<AvisAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<AvisAdmin>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<AvisAdmin>>.Echec("File de modération illisible.")
            : Resultat<PageApi<AvisAdmin>>.Ok(resultat);
    }

    /// <summary>Signale, rejette ou restaure un avis.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CES TROIS GESTES RÉÉCRIVENT LA RÉPUTATION DE LA PLACE DE MARCHÉ.
    ///
    /// La note d'un produit et celle d'un vendeur se calculent sur les avis
    /// `Published` : rejeter un avis le retire de la moyenne, le restaurer l'y
    /// remet. C'est pourquoi le service les a sortis du groupe authentifié —
    /// n'importe quel inscrit pouvait auparavant les appeler.
    ///
    /// `MapAdminGroup` inclut le rôle Modérateur, et c'est son objet : arbitrer
    /// des contenus n'est pas administrer la plateforme.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> ModererAvisAsync(
        Guid avis, string geste, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/reviews/{avis}/{geste}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LES BOUTIQUES D'UN VENDEUR.
    //
    // LECTURE SUR LE GROUPE VENDEUR, SANCTION SUR LE GROUPE ADMIN.
    //
    // `GET .../stores` vit sur `MapSellerGroup` et sa garde
    // `DenyUnlessOwnSellerAsync` court-circuite sur le rôle `Admin`. Les deux
    // gestes de sanction, eux, sont sur un `MapAdminGroup` distinct : le domaine
    // les déclare « décision d'admin », et `SuspendStoreCommand` ne porte
    // volontairement pas de `SellerId`.
    //
    // CE COURT-CIRCUIT NE COUVRE QUE `Admin`, PAS `Moderator`.
    //
    // Contrairement à celui de financial-service, qui laisse passer les deux. Un
    // modérateur recevra donc 403 sur cette lecture — c'est le serveur qui le
    // décide, et l'écran remonte le message tel quel.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Les boutiques d'un vendeur.</summary>
    public async Task<Resultat<IReadOnlyList<BoutiqueAdmin>>> ListerBoutiquesAsync(
        Guid vendeur, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/v1/merchants/{vendeur}/stores", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<BoutiqueAdmin>>.Echec(reponse.Message!);
        }

        var enveloppe = Lire<EnveloppeApi<List<BoutiqueAdmin>>>(reponse.Valeur);

        return enveloppe?.Data is null
            ? Resultat<IReadOnlyList<BoutiqueAdmin>>.Echec("Liste des boutiques illisible.")
            : Resultat<IReadOnlyList<BoutiqueAdmin>>.Ok(enveloppe.Data);
    }

    /// <summary>Suspend une boutique, ou lève sa suspension.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// SUSPENDRE N'EST PAS FERMER, ET LA DIFFÉRENCE EST DANS QUI PEUT DÉFAIRE.
    ///
    /// `StoreStatus.Closed` est la fermeture décidée par le VENDEUR — congés,
    /// travaux, saison — « réversible d'un geste, et c'est ce qui la distingue de
    /// la suspension ». `StoreStatus.Suspended` est la fermeture décidée par la
    /// PLATEFORME : « le vendeur ne peut pas la rouvrir lui-même, sinon la
    /// sanction ne durerait que le temps d'un clic ».
    ///
    /// Confondre les deux à l'écran ferait rouvrir par le vendeur ce que la
    /// plateforme vient de sanctionner — sauf que le domaine l'en empêche, et que
    /// l'administrateur croirait la sanction levée.
    ///
    /// LE MOTIF EST FACULTATIF CÔTÉ SERVEUR — `ReasonRequest(string? Reason)` —
    /// ET L'ÉCRAN L'EXIGE QUAND MÊME. Il est stocké dans `StatusReason`, absent
    /// de la vitrine publique, et c'est la seule trace de la raison d'une
    /// sanction. Une suspension sans motif se retrouve un mois plus tard sans que
    /// personne ne sache pourquoi elle a été posée.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> BasculerBoutiqueAsync(
        Guid vendeur, Guid boutique, bool suspendre, string? motif, CancellationToken jeton = default)
    {
        var geste = suspendre ? "suspend" : "lift-suspension";
        object? corps = suspendre ? new { reason = motif } : null;

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/v1/merchants/{vendeur}/stores/{boutique}/{geste}",
            corps, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMMISSIONS ET FACTURES (billing-service, servi par financial-service).
    //
    // LES DEUX PRÉFIXES VIENNENT D'ÊTRE OUVERTS À LA PASSERELLE, ET DANS CET
    //    ORDRE : LA GARDE D'ABORD, LE RELAIS ENSUITE.
    //
    // `GET /api/financial/commissions` était la seule route de son groupe sans
    // `.RequireAdmin()`, à côté de cinq écritures qui en portent une — et elle
    // rend les règles de portée `Seller`, c'est-à-dire le taux négocié de chaque
    // vendeur. La relayer avant de la garder l'aurait exposée à tout compte
    // authentifié le temps d'un déploiement.
    //
    // CES ROUTES RENDENT DES CORPS NUS, SANS ENVELOPPE — sauf la liste de
    // factures, écrite dans le même lot et rendue en `ApiResults.Page`.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Les règles de commission de la plateforme.</summary>
    public async Task<Resultat<IReadOnlyList<RegleCommission>>> ListerCommissionsAsync(
        CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Get, "/api/financial/commissions", null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<IReadOnlyList<RegleCommission>>.Echec(reponse.Message!);
        }

        var liste = Lire<List<RegleCommission>>(reponse.Valeur);

        return liste is null
            ? Resultat<IReadOnlyList<RegleCommission>>.Echec("Grille de commission illisible.")
            : Resultat<IReadOnlyList<RegleCommission>>.Ok(liste);
    }

    /// <summary>Crée une règle de commission.</summary>
    /// <remarks>
    /// LE CORPS EST LA COMMANDE ELLE-MÊME.
    ///
    /// `CreateInvoiceAsync` et `CreateCommissionRuleAsync` prennent directement
    /// la commande MediatR en corps de requête, sans record de requête
    /// intermédiaire. Les noms des champs sont donc ceux de la commande.
    ///
    /// `Scope` doit valoir « Global », « Category » ou « Seller » : le validateur
    /// le vérifie littéralement, et le handler reparse ensuite avec
    /// `Enum.TryParse`. Une autre valeur rend un 400 explicite.
    /// </remarks>
    public async Task<Resultat<bool>> CreerCommissionAsync(
        string portee, Guid? cible, decimal taux, decimal fixe, string devise,
        decimal? minimum, decimal? maximum, DateTime effetUtc, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/financial/commissions",
            new
            {
                scope = portee,
                targetId = cible,
                rate = taux,
                fixedFee = fixe,
                currency = devise,
                minFee = minimum,
                maxFee = maximum,
                effectiveFromUtc = effetUtc,
            },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Modifie une règle de commission.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `effectiveFromUtc` NUL SIGNIFIE « NE TOUCHE PAS À LA DATE ».
    ///
    /// Et c'est la correction d'un bogue que le dépôt documente : le champ était
    /// non nullable, « et le BFF comblait l'absence par `?? DateTime.UtcNow`. Or
    /// la console d'administration n'a jamais su renvoyer ce champ — son type
    /// TypeScript ne le porte même pas. Résultat : la moindre correction de taux
    /// sur une règle PROGRAMMÉE la rendait applicable SUR-LE-CHAMP, et la faisait
    /// passer devant ses sœurs de même portée. »
    ///
    /// Cette console-ci envoie donc la date SEULEMENT quand l'administrateur l'a
    /// changée. Le périmètre, lui, n'est pas modifiable : `UpdateCommissionRule`
    /// ne le reprend pas.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> ModifierCommissionAsync(
        Guid regle, decimal taux, decimal fixe, string devise,
        decimal? minimum, decimal? maximum, DateTime? effetUtc, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Put, $"/api/financial/commissions/{regle}",
            new
            {
                rate = taux,
                fixedFee = fixe,
                currency = devise,
                minFee = minimum,
                maxFee = maximum,
                effectiveFromUtc = effetUtc,
            },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Active, désactive ou supprime une règle de commission.</summary>
    /// <remarks>
    /// DÉSACTIVER N'EST PAS SUPPRIMER, ET LA DIFFÉRENCE COMPTE ICI.
    ///
    /// `IsApplicableAt` teste `IsActive && EffectiveFromUtc <= now` : une règle
    /// désactivée cesse de s'appliquer et reste lisible — donc réactivable, et
    /// surtout consultable pour comprendre ce qui s'appliquait le mois dernier.
    /// La suppression, elle, efface la ligne.
    /// </remarks>
    public async Task<Resultat<bool>> AgirSurCommissionAsync(
        Guid regle, string geste, CancellationToken jeton = default)
    {
        var reponse = geste == "supprimer"
            ? await EnvoyerAsync(
                HttpMethod.Delete, $"/api/financial/commissions/{regle}", null, authentifier: true, jeton)
            : await EnvoyerAsync(
                HttpMethod.Post, $"/api/financial/commissions/{regle}/{geste}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Calcule la commission applicable à un montant — aperçu servi par le moteur.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// CET APERÇU N'EST PAS UNE RECOPIE : IL DÉLÈGUE AU CALCUL RÉEL.
    ///
    /// C'est ce qui le distingue de l'aperçu de la page Tarification, où aucune
    /// route ne simule et où les cinq lignes de `PricingPolicy` ont dû être
    /// recopiées côté client.
    ///
    /// Le dépôt a d'ailleurs payé cette leçon : un gestionnaire antérieur
    /// recopiait ici `GetCandidatesAsync` + `CommissionResolver.Resolve`, « à un
    /// détail près : le repli. Le moteur applique le taux par défaut ; cette
    /// copie rendait 0. Sur une plateforme où aucune règle n'est définie — le cas
    /// courant —, l'écran annonçait donc commission : 0 pendant que la
    /// comptabilisation prélevait 10 %. »
    ///
    /// LA ROUTE EST GARDÉE PAR VENDEUR, ET L'ADMINISTRATEUR LA TRAVERSE.
    /// `DenyUnlessOwnSellerAsync` court-circuite sur `Admin` et `Moderator`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<ApercuCommission>> CalculerCommissionAsync(
        Guid vendeur, Guid categorie, decimal montant, string devise, CancellationToken jeton = default)
    {
        var parametres =
            $"sellerId={vendeur}&categoryId={categorie}"
            + $"&grossAmount={montant.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"&currency={Uri.EscapeDataString(devise)}";

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/financial/commissions/compute?{parametres}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<ApercuCommission>.Echec(reponse.Message!);
        }

        var apercu = Lire<ApercuCommission>(reponse.Valeur);

        return apercu is null
            ? Resultat<ApercuCommission>.Echec("Aperçu illisible.")
            : Resultat<ApercuCommission>.Ok(apercu);
    }

    /// <summary>Une page de factures, tous vendeurs confondus.</summary>
    public async Task<Resultat<PageApi<FactureAdmin>>> ListerFacturesAsync(
        int page, int taille, string? statut, Guid? vendeur, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(statut))
        {
            parametres.Add($"status={Uri.EscapeDataString(statut)}");
        }

        if (vendeur is { } identifiant)
        {
            parametres.Add($"sellerId={identifiant}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/financial/invoices?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<FactureAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<FactureAdmin>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<FactureAdmin>>.Echec("Liste des factures illisible.")
            : Resultat<PageApi<FactureAdmin>>.Ok(resultat);
    }

    /// <summary>Crée une facture brouillon pour un vendeur et une période.</summary>
    public async Task<Resultat<bool>> CreerFactureAsync(
        Guid vendeur, DateTime debutUtc, DateTime finUtc, string devise, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/financial/invoices",
            new { sellerId = vendeur, periodStartUtc = debutUtc, periodEndUtc = finUtc, currency = devise },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Ajoute une ligne à une facture brouillon.</summary>
    /// <remarks>
    /// LA LIGNE AJOUTÉE NE SERA JAMAIS RELUE.
    ///
    /// `InvoiceSummary` ne porte pas `Lines`, et `GetInvoiceQuery` rend ce même
    /// résumé : aucune route n'expose le détail d'une facture. Seul le total
    /// bouge. L'écran l'annonce avant l'envoi — composer une facture à l'aveugle
    /// est acceptable si on le sait, pas si on le découvre.
    /// </remarks>
    public async Task<Resultat<bool>> AjouterLigneFactureAsync(
        Guid facture, string libelle, decimal montant, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/financial/invoices/{facture}/lines",
            new { description = libelle, amount = montant }, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Émet une facture, ou la marque payée.</summary>
    public async Task<Resultat<bool>> AgirSurFactureAsync(
        Guid facture, string geste, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"/api/financial/invoices/{facture}/{geste}", null, authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>Une page de recommandations, tous contextes confondus.</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE PRÉFIXE PUBLIC N'EST PAS CELUI DU SERVICE.
    ///
    /// La console parle à la passerelle, qui expose `/api/recommendations` et
    /// réécrit vers `/api/engagement/recommendations`. Écrire le chemin du
    /// service ici donnerait un 404 muet : le cluster et la destination seraient
    /// corrects, seule la route ne correspondrait à rien. Même famille que
    /// `/api/reviews`, employé plus haut pour la modération.
    ///
    /// LA GARDE ADMIN EST DANS LE SERVICE, PAS DANS LA PASSERELLE. La route
    /// relayée porte « Authenticated » et couvre tous les verbes ; c'est
    /// `MapAdminGroup` qui refuse un compte ordinaire, à l'aller comme au
    /// retour. Durcir la passerelle ici créerait un second endroit à tenir
    /// d'accord, et fermerait `/product/{id}` aux applications acheteur, qui
    /// partagent le même préfixe.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<PageApi<RecommandationAdmin>>> ListerRecommandationsAsync(
        int page, int taille, string? type, CancellationToken jeton = default)
    {
        var parametres = new List<string> { $"page={page}", $"pageSize={taille}" };

        if (!string.IsNullOrWhiteSpace(type))
        {
            parametres.Add($"type={Uri.EscapeDataString(type)}");
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Get, $"/api/recommendations?{string.Join('&', parametres)}",
            null, authentifier: true, jeton);

        if (!reponse.Reussi)
        {
            return Resultat<PageApi<RecommandationAdmin>>.Echec(reponse.Message!);
        }

        var resultat = Lire<PageApi<RecommandationAdmin>>(reponse.Valeur);

        return resultat?.Data is null
            ? Resultat<PageApi<RecommandationAdmin>>.Echec("Liste des recommandations illisible.")
            : Resultat<PageApi<RecommandationAdmin>>.Ok(resultat);
    }

    /// <summary>Pose ou remplace la recommandation d'une clé (type, contexte).</summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// C'EST UN REMPLACEMENT, PAS UN AJOUT, ET LA ROUTE S'APPELLE POURTANT `POST`.
    ///
    /// `UpsertRecommendationCommandHandler` cherche l'existant sur la clé
    /// fonctionnelle — par utilisateur si le type est `Personalized`, par produit
    /// sinon — et appelle `Refresh`, qui REMPLACE la liste entière et le score.
    /// Renvoyer un seul produit sur une clé qui en portait dix en efface neuf,
    /// sans confirmation et sans trace.
    ///
    /// LE DOMAINE FILTRE EN SILENCE : `Create` comme `Refresh` appliquent
    /// `.Where(p => p != Guid.Empty).Distinct()`. Un doublon disparaît sans
    /// erreur, et le compte renvoyé peut être inférieur à celui qui a été envoyé.
    /// L'écran compare donc ce qu'il a demandé à ce qui est relu.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public async Task<Resultat<bool>> EnregistrerRecommandationAsync(
        string type, Guid? produitContexte, Guid? utilisateur, IReadOnlyList<Guid> produits,
        double score, CancellationToken jeton = default)
    {
        var reponse = await EnvoyerAsync(
            HttpMethod.Post, "/api/recommendations",
            new
            {
                type,
                contextProductId = produitContexte,
                userId = utilisateur,
                recommendedProductIds = produits,
                score,
            },
            authentifier: true, jeton);

        return reponse.Reussi
            ? Resultat<bool>.Ok(true)
            : Resultat<bool>.Echec(reponse.Message!);
    }

    /// <summary>La session est-elle assez fraîche pour un geste destructeur ?</summary>
    public bool ElevationValide => _session.ElevationValide;

    /// <summary>Ferme la session côté client.</summary>
    public void Deconnecter() => _session.Oublier();

    public void Dispose() => _http.Dispose();

    // ───────────────────────────────────────────────────────────── interne

    private void Poser(JetonsApi jetons)
        => _session.Poser(
            jetons.AccessToken!,
            jetons.RefreshToken!,
            jetons.ExpireLe ?? DateTimeOffset.UtcNow.AddMinutes(10));

    /// <summary>
    /// Envoie une requête, en rafraîchissant le jeton d'accès si besoin.
    /// </summary>
    /// <remarks>
    /// LE RAFRAÎCHISSEMENT EST TENTÉ AVANT L'APPEL, PAS SEULEMENT APRÈS UN 401.
    ///
    /// N'agir que sur un 401 signifie faire échouer une requête sur deux au
    /// moment de l'expiration, puis la rejouer — ce qui n'est pas anodin sur des
    /// gestes NON idempotents. Ici l'approche est inversée : on renouvelle avant,
    /// et le 401 ne sert plus que de filet quand le serveur a révoqué le jeton
    /// (déconnexion depuis un autre poste, compte suspendu).
    ///
    /// ET IL N'Y A PAS DE SECONDE TENTATIVE APRÈS LE FILET.
    ///
    /// Un 401 qui survit au renouvellement signifie que la session est finie. La
    /// rejouer produirait une boucle silencieuse contre un serveur qui a déjà
    /// dit non.
    /// </remarks>
    private async Task<Resultat<string>> EnvoyerAsync(
        HttpMethod methode, string chemin, object? corps, bool authentifier, CancellationToken jeton)
    {
        if (authentifier && !await AssurerJetonAsync(jeton))
        {
            return Resultat<string>.Echec("Session expirée. Reconnectez-vous.");
        }

        try
        {
            using var requete = Construire(methode, chemin, corps, authentifier);
            using var reponse = await _http.SendAsync(requete, jeton);

            if (reponse.StatusCode == HttpStatusCode.Unauthorized && authentifier)
            {
                _session.Oublier();
                return Resultat<string>.Echec("Session expirée. Reconnectez-vous.");
            }

            var texte = await reponse.Content.ReadAsStringAsync(jeton);

            return reponse.IsSuccessStatusCode
                ? Resultat<string>.Ok(texte)
                : Resultat<string>.Echec(Message(reponse.StatusCode, texte));
        }
        catch (TaskCanceledException) when (!jeton.IsCancellationRequested)
        {
            // `TaskCanceledException` EST AUSSI CE QUE LÈVE UN DÉLAI DÉPASSÉ.
            //
            // Sans le garde sur le jeton d'annulation, une fermeture de fenêtre
            // s'afficherait comme « la passerelle ne répond pas » — et l'on
            // chercherait une panne réseau là où l'utilisateur a simplement
            // quitté l'écran.
            return Resultat<string>.Echec("La passerelle ne répond pas.");
        }
        catch (HttpRequestException exception)
        {
            return Resultat<string>.Echec($"Passerelle injoignable : {exception.Message}");
        }
    }

    private HttpRequestMessage Construire(
        HttpMethod methode, string chemin, object? corps, bool authentifier)
    {
        var requete = new HttpRequestMessage(methode, chemin);

        if (corps is not null)
        {
            requete.Content = JsonContent.Create(corps, options: _json);
        }

        if (authentifier && _session.Jeton is { } acces)
        {
            requete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", acces);
        }

        return requete;
    }

    /// <summary>Renouvelle le jeton s'il expire dans moins d'une minute.</summary>
    private async Task<bool> AssurerJetonAsync(CancellationToken jeton)
    {
        if (!_session.EstOuverte)
        {
            return false;
        }

        if (_session.ExpireLe - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(1))
        {
            return true;
        }

        if (_session.Rafraichissement is not { } rafraichissement)
        {
            return false;
        }

        var reponse = await EnvoyerAsync(
            HttpMethod.Post, $"{Auth}/refresh",
            new { refreshToken = rafraichissement }, authentifier: false, jeton);

        if (!reponse.Reussi)
        {
            _session.Oublier();
            return false;
        }

        var jetons = Lire<JetonsApi>(reponse.Valeur);

        if (jetons is not { AccessToken: not null, RefreshToken: not null })
        {
            _session.Oublier();
            return false;
        }

        Poser(jetons);
        return true;
    }

    private static T? Lire<T>(string? texte) where T : class
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(texte, _json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Traduit un code HTTP en une phrase que l'administrateur peut lire.
    /// </summary>
    /// <remarks>
    /// LE CORPS DE LA RÉPONSE N'EST PAS AFFICHÉ TEL QUEL.
    ///
    /// Il peut porter un `traceId`, un nom de service interne, voire un détail de
    /// chaîne de connexion selon la couche qui a échoué. Le socle serveur prend
    /// déjà soin de ne pas les exposer (`EnableDetailedErrors = false`) ; ce
    /// n'est pas une raison pour que le client les recopie à l'écran si un jour
    /// ils reviennent.
    /// </remarks>
    private static string Message(HttpStatusCode code, string _) => code switch
    {
        HttpStatusCode.Unauthorized => "Identifiants refusés.",
        HttpStatusCode.Forbidden => "Ce compte n'a pas le rôle Administrateur.",
        HttpStatusCode.NotFound => "Ressource introuvable.",
        HttpStatusCode.TooManyRequests => "Trop de tentatives. Patientez avant de réessayer.",

        // ═════════════════════════════════════════════════════════════════════
        // 503 ÉTAIT TRAITÉ, 502 NON — ET C'EST 502 QUE YARP RÉPOND.
        //
        // Quand la destination d'un cluster refuse la connexion — conteneur
        // arrêté, image jamais construite, service en cours de démarrage — YARP
        // rend `502 Bad Gateway`, pas 503. Le message « un service amont est
        // indisponible » existait donc depuis le début et ne s'affichait
        // JAMAIS : le cas réel tombait dans la formule générique, qui recopie un
        // nombre sans rien en dire.
        //
        // 503 est conservé : c'est ce que rend un service qui a démarré mais se
        // déclare non prêt (sonde `/health/ready`), et la conduite à tenir n'est
        // pas la même — attendre, plutôt que relancer.
        // ═════════════════════════════════════════════════════════════════════
        HttpStatusCode.BadGateway =>
            "Le service amont ne répond pas : conteneur arrêté, ou image jamais construite. "
            + "La passerelle l'a bien trouvé dans sa configuration, personne n'a décroché.",

        HttpStatusCode.ServiceUnavailable =>
            "Un service amont a démarré mais ne se déclare pas prêt. Il finit peut-être de "
            + "migrer sa base ; réessayez dans un instant.",

        // ═════════════════════════════════════════════════════════════════════
        // 405 SUR UNE LECTURE A UNE SEULE CAUSE ICI, ET ELLE VAUT D'ÊTRE NOMMÉE.
        //
        // Le routage d'ASP.NET Core ne rend `405` que si le CHEMIN correspond à
        // un point d'entrée enregistré pour un AUTRE verbe. Sur les listes de
        // cette console, le chemin porte toujours un `POST` — créer une facture,
        // écrire une recommandation. Un 405 en lecture signifie donc : la route
        // GET existe dans le dépôt, et PAS dans le binaire qui tourne.
        //
        // Autrement dit, ce n'est pas une panne : c'est une image à reconstruire.
        // Le distinguer d'un 404 — où le chemin n'existe nulle part, souvent une
        // entrée de passerelle manquante — évite d'aller chercher le défaut du
        // mauvais côté.
        // ═════════════════════════════════════════════════════════════════════
        HttpStatusCode.MethodNotAllowed =>
            "Ce chemin existe côté serveur, mais pas en lecture : le binaire qui tourne n'a "
            + "pas cette route. L'image du service est antérieure à son ajout — reconstruisez-la.",

        _ => $"La passerelle a répondu {(int)code}.",
    };
}
