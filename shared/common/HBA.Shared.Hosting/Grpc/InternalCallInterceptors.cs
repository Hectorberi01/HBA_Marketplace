using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Côté CLIENT : joint le secret interne et l'identifiant de corrélation à
/// chaque appel sortant.
/// </summary>
public sealed class InternalCallClientInterceptor : Interceptor
{
    private readonly IOptions<InternalCallOptions> _options;
    private readonly IHttpContextAccessor _accessor;

    public InternalCallClientInterceptor(
        IOptions<InternalCallOptions> options, IHttpContextAccessor accessor)
    {
        _options = options;
        _accessor = accessor;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = context.Options.Headers ?? new Metadata();

        var key = _options.Value.ApiKey;

        if (!string.IsNullOrWhiteSpace(key))
        {
            headers.Add(InternalRoutes.MetadataKey, key);
        }

        // ═════════════════════════════════════════════════════════════════════
        // L'ATTESTATION D'IDENTITÉ EST FRAPPÉE ICI, DONC UNE FOIS PAR APPEL.
        //
        // Elle est liée à la MÉTHODE (`context.Method.FullName`) et expire en
        // trente secondes : c'est ce qui empêche un jeton observé sur le réseau —
        // qui est en clair — de servir pour un autre RPC ou dix minutes plus
        // tard. La mettre en cache par appelant ferait tomber ce lien, donc la
        // seule protection contre le rejeu qui existe. Une signature P-256 coûte
        // quelques dizaines de microsecondes, contre plusieurs millisecondes pour
        // l'aller-retour réseau qui suit : l'optimisation n'aurait rien rapporté
        // et aurait coûté la propriété.
        //
        // ON REFUSE PLUTÔT QUE D'ENVOYER UN APPEL NU.
        //
        // Sans clé privée, l'appel partirait, traverserait le réseau et
        // reviendrait en `Unauthenticated` — une faute de configuration LOCALE
        // diagnostiquée à distance, dans le journal du service appelé.
        // `FailedPrecondition` levée ici nomme la cause au bon endroit, et n'est
        // pas comptée comme une panne par le disjoncteur (voir
        // `DisjoncteurClientInterceptor.EstUnePanne`) : une variable
        // d'environnement oubliée ne doit pas ressembler à un service tombé.
        // ═════════════════════════════════════════════════════════════════════
        var clePrivee = _options.Value.PrivateKey;

        if (!string.IsNullOrWhiteSpace(clePrivee))
        {
            headers.Add(
                IdentiteInterne.MetadataKey,
                IdentiteInterne.Signer(NomDeCetHote(), context.Method.FullName, clePrivee));
        }
        else if (_options.Value.IdentitesNonSignees)
        {
            // Développement seulement — `AddHbaGrpc` refuse ce drapeau ailleurs.
            // Le nom nu ne contient jamais de point ; c'est ce qui le distingue
            // sans ambiguïté d'une attestation signée, qui en contient toujours un.
            headers.Add(IdentiteInterne.MetadataKey, NomDeCetHote());
        }
        else
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "Internal identity not configured."));
        }

        // LA CORRÉLATION DOIT TRAVERSER LE SAUT gRPC.
        //
        // Sans elle, la trace s'arrête au service appelant : on voit la requête
        // entrer dans catalog-service sans pouvoir la relier à l'appel média qui
        // en découle. C'est exactement ce que la corrélation sert à éviter.
        //
        // Les clés de métadonnées gRPC doivent être en MINUSCULES. Une clé
        // contenant une majuscule est rejetée à l'exécution, au PREMIER appel
        // réel — donc pas à la compilation, ni au démarrage.
        var correlationId = _accessor.HttpContext?
            .Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers.Add("x-correlation-id", correlationId);
        }

        var options = context.Options.WithHeaders(headers);

        // ═════════════════════════════════════════════════════════════════════
        // UN APPEL gRPC SANS ÉCHÉANCE ATTEND INDÉFINIMENT.
        //
        // Contrairement à `HttpClient`, qui a un délai de 100 s par défaut, un
        // canal gRPC n'en a AUCUN. Un service bloqué — pas tombé, bloqué —
        // retiendrait les tâches de tous ses appelants jusqu'à saturation, et la
        // panne se propagerait à des services parfaitement sains.
        //
        // L'échéance est posée ici plutôt qu'à chaque appel : un seul oubli dans
        // les 540 sites d'appel suffirait à rouvrir le trou. Un appelant qui a
        // besoin d'un délai différent peut toujours le fournir — on ne l'écrase
        // pas.
        //
        // 5 secondes correspond au `TotalTimeout` déjà retenu pour les clients
        // HTTP de la passerelle. Valeur de départ, à ajuster sur des mesures.
        // ═════════════════════════════════════════════════════════════════════
        if (options.Deadline is null)
        {
            options = options.WithDeadline(DateTime.UtcNow.AddSeconds(5));
        }

        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, options));
    }

    /// <summary>
    /// Nom d'identité de cet hôte : `Internal:ServiceName`, sinon l'assembly d'entrée.
    /// </summary>
    /// <remarks>
    /// RÉSOLU UNE FOIS, PARCE QUE `GetEntryAssembly` COÛTE PLUS QUE LA SIGNATURE.
    ///
    /// Le pourquoi du choix de l'assembly plutôt que de `SERVICE_NAME` est écrit
    /// dans <see cref="InternalCallOptions.ServiceName"/> — il tient à deux faits
    /// vérifiables du dépôt, pas à une préférence.
    ///
    /// Le repli `"?"` est intentionnellement invalide : il ne figure dans aucune
    /// entrée de <see cref="AutorisationsGrpc"/>, donc l'appel sera refusé plutôt
    /// que d'emprunter par accident l'identité d'un autre. Il n'est atteignable
    /// que si `GetEntryAssembly` rend `null`, ce qui n'arrive pas dans un hôte
    /// managé — mais arrive sous un runtime hébergé.
    /// </remarks>
    private string NomDeCetHote()
        => _nom ??= string.IsNullOrWhiteSpace(_options.Value.ServiceName)
            ? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "?"
            : _options.Value.ServiceName!;

    private string? _nom;
}

/// <summary>
/// Côté SERVEUR : refuse tout appel qui ne présente pas le secret interne.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ÉQUIVALENT gRPC DU FILTRE POSÉ SUR LES ROUTES `/internal/*`.
///
/// Les deux barrières restent les mêmes :
///   1. le réseau — le port gRPC n'est jamais exposé par la passerelle ;
///   2. ce secret partagé.
///
/// ON RÉPONDAIT `NotFound`. C'EST MAINTENANT `Unauthenticated`, ET LE
/// RAISONNEMENT D'ORIGINE EST CONSERVÉ CI-DESSOUS PARCE QU'IL N'ÉTAIT PAS ABSURDE.
///
/// L'argument était : « un appelant sans secret n'a pas à apprendre que ce
/// service expose une API gRPC ; `Unauthenticated` confirmerait l'existence du
/// point d'entrée et inviterait à chercher la clé. »
///
/// Ce que cet argument ne pesait pas :
///
///   • CE QU'IL PROTÈGE EST DÉJÀ CONNU. Le port gRPC n'est pas exposé par la
///     passerelle (barrière 1) ; quiconque peut l'atteindre est DÉJÀ sur le
///     réseau interne, où tous les services écoutent le même port. Le secret
///     gardé était « ce service a une API gRPC » — que la topologie annonce.
///
///   • CE QU'IL COÛTE EST CONCRET. `NotFound` est AUSSI le code naturel d'une
///     ressource absente. Une clé mal déployée produisait donc une
///     `RpcException(NotFound)` que le seul appelant filtrant les statuts —
///     `GrpcDeliveryPricingQuoteValidator`, qui ne rattrape que `Unavailable` et
///     `DeadlineExceeded` — laissait remonter brute dans `CreateDeliveryCommand`.
///     Un incident d'AUTHENTIFICATION déguisé en défaut de DOMAINE, sans trace
///     exploitable, au moment précis où l'on cherche pourquoi les courses ne
///     partent plus.
///
/// Le message, lui, reste neutre : il ne dit ni quelle clé est attendue, ni
/// qu'une clé a été présentée. On perd l'ambiguïté du code, pas la discrétion du
/// contenu.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class InternalCallServerInterceptor : Interceptor
{
    private readonly IOptions<InternalCallOptions> _options;

    private IReadOnlyDictionary<string, string>? _registre;

    public InternalCallServerInterceptor(IOptions<InternalCallOptions> options)
        => _options = options;

    public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var expected = _options.Value.ApiKey;

        if (string.IsNullOrWhiteSpace(expected))
        {
            // Clé absente = configuration incomplète. On ferme : laisser passer
            // « puisqu'il n'y a rien à vérifier » ouvrirait l'API au réseau entier
            // au moindre oubli de variable d'environnement, sans aucun symptôme.
            //
            // `FailedPrecondition`, ET NON `Unavailable` COMME AUPARAVANT.
            //
            // `Unavailable` veut dire « réessaie plus tard ». Ici l'erreur est
            // PERMANENTE : elle dure jusqu'au redéploiement. Un appelant doté d'un
            // réessai martèlerait indéfiniment un service qui ne guérira pas tout
            // seul.
            //
            // Et depuis que les clients ont un disjoncteur, le mauvais code coûte
            // davantage : `Unavailable` est compté comme une panne, donc une
            // variable d'environnement oubliée ferait OUVRIR le disjoncteur de
            // tous les appelants — traitant une faute de configuration comme une
            // panne passagère, et masquant sa vraie nature derrière un « service
            // indisponible ». `FailedPrecondition` n'est pas compté : l'appel
            // échoue vite, franchement, et l'exploitation lit la bonne cause.
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "Internal API not configured."));
        }

        var presented = context.RequestHeaders.GetValue(InternalRoutes.MetadataKey);

        if (!InternalRoutes.SecretsMatch(presented, expected))
        {
            // `Unauthenticated`, ET NON `NotFound` : voir l'encadré de la classe.
            // Le message reste volontairement muet — il ne distingue pas « aucune
            // clé » de « mauvaise clé ».
            throw new RpcException(new Status(
                StatusCode.Unauthenticated, "Internal call rejected."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // DEUXIÈME SERRURE : QUI APPELLE, ET A-T-IL LE DROIT.
        //
        // La clé ci-dessus atteste l'APPARTENANCE au réseau — elle est la même
        // pour les dix-neuf hôtes, donc elle ne dit rien de l'appelant. Ce qui
        // suit le dit : signature asymétrique liée à la méthode et à l'instant,
        // puis table d'autorisations. Voir `IdentiteInterne` pour ce que ce
        // dispositif NE couvre PAS — le réseau reste en clair.
        //
        // L'ORDRE EST VOULU : LE MOINS CHER D'ABORD.
        //
        // Comparer deux chaînes coûte moins qu'une vérification P-256. Un appel
        // sans clé partagée — le cas d'un balayage de port — est écarté avant
        // qu'on ne dépense de la cryptographie sur lui.
        // ═════════════════════════════════════════════════════════════════════
        // Course bénigne : deux fils peuvent construire le registre en même temps
        // et produisent le même dictionnaire. Un verrou coûterait plus que la
        // duplication qu'il évite, et le champ n'est jamais partiellement visible
        // — l'affectation d'une référence est atomique.
        var registre = _registre ??= IdentiteInterne.LireRegistre(_options.Value.PublicKeys);

        var presentee = context.RequestHeaders.GetValue(IdentiteInterne.MetadataKey);

        // CHEMIN DE DÉVELOPPEMENT — VOIR `InternalCallOptions.IdentitesNonSignees`.
        //
        // Il est placé AVANT le refus « registre vide » parce qu'en développement
        // il n'y a précisément aucun registre : exiger l'un pour atteindre l'autre
        // rendrait le drapeau inopérant.
        if (_options.Value.IdentitesNonSignees
            && !string.IsNullOrWhiteSpace(presentee)
            && !presentee.Contains('.'))
        {
            return Autoriser(presentee, request, context, continuation);
        }

        if (registre.Count == 0)
        {
            // Même raisonnement que pour la clé absente ci-dessus : registre vide
            // = configuration incomplète, erreur PERMANENTE, donc
            // `FailedPrecondition` — que le disjoncteur des appelants ne compte
            // pas comme une panne.
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "Internal identity not configured."));
        }

        var appelant = IdentiteInterne.Verifier(presentee, context.Method, registre);

        if (appelant is null)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated, "Internal call rejected."));
        }

        return Autoriser(appelant, request, context, continuation);
    }

    /// <summary>
    /// Applique la table d'autorisations à un appelant DÉJÀ identifié.
    /// </summary>
    private static Task<TResponse> Autoriser<TRequest, TResponse>(
        string appelant,
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        if (!AutorisationsGrpc.EstAutorise(appelant, context.Method))
        {
            // `PermissionDenied`, ET NON `Unauthenticated`.
            //
            // La distinction n'est pas cosmétique : ici l'appelant EST identifié
            // et sa signature EST valide. Répondre `Unauthenticated` enverrait
            // l'exploitation chercher une clé mal déployée alors que le problème
            // est une table d'autorisations qui n'a pas suivi un nouvel appel —
            // c'est-à-dire un oubli de `scripts/check-autorisations-grpc.py`, pas
            // un incident de secret.
            //
            // Comme `Unauthenticated`, ce code n'est pas compté par le
            // disjoncteur : un refus d'autorisation est déterministe, réessayer
            // ne le changera pas, et il ne doit pas couper les appels légitimes
            // du même appelant vers le même service.
            throw new RpcException(new Status(
                StatusCode.PermissionDenied, "Internal call not permitted."));
        }

        // Le nom de l'appelant est déposé pour les couches au-dessus — journal,
        // trace, futur audit des appels internes. Rien ne le lit aujourd'hui ;
        // c'est la seule information que cette barrière produit et que personne
        // d'autre ne peut reconstituer.
        context.UserState["appelant-interne"] = appelant;

        return continuation(request, context);
    }
}
