using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using System.Collections.Concurrent;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Disjoncteur par service appelé, posé sur les appels gRPC sortants.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MAILLAGE INTERNE ÉTAIT MOINS PROTÉGÉ QUE LE TRAFIC NORD-SUD.
///
/// La passerelle HTTP a un disjoncteur depuis toujours (<c>HbaResilience</c>).
/// Les vingt clients gRPC internes n'en avaient AUCUN — alors que ce sont eux qui
/// portent les appels en série du parcours d'achat.
///
/// CE QUE COÛTAIT SON ABSENCE, CONCRÈTEMENT.
///
/// <c>MerchantApi.GetMemberAccess</c> est appelé sur CHAQUE requête vendeur de
/// catalog, inventory, order et payment. Une lenteur de seller-service faisait
/// donc attendre 5 secondes — l'échéance — à chaque requête vendeur des QUATRE
/// services, jusqu'à saturation de leurs pools de tâches. C'est exactement la
/// propagation que l'échéance prétend éviter : elle borne UNE requête, elle ne
/// coupe pas la source. Au checkout, <c>GetActiveCart</c> puis <c>ReserveStock</c>
/// sont en série : 5 s + 5 s par ligne de commande, sans coupure.
///
/// TROIS COMMENTAIRES DU DÉPÔT ANNONÇAIENT CE DISJONCTEUR COMME ACQUIS —
/// <c>PromotionGrpcClient</c>, <c>MediaGrpcClient</c>, <c>promotion.proto</c>.
/// Il n'existait pas. C'est la raison d'être de ce fichier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN INTERCEPTEUR, ET NON UN `AddResilienceHandler` SUR LE HttpClient.
///
/// C'était le geste évident — <c>AddGrpcClient</c> rend un
/// <see cref="Microsoft.Extensions.DependencyInjection.IHttpClientBuilder"/>, et
/// la passerelle procède ainsi. Il aurait produit un disjoncteur QUI NE VOIT
/// PRESQUE RIEN, pour deux raisons :
///
///   1. UN ÉCHEC gRPC EST UN HTTP 200. Le code de statut voyage dans les
///      en-têtes ou les bandes-annonces HTTP/2, jamais dans le statut HTTP. Un
///      <c>Internal</c>, un <c>Unavailable</c>, un <c>ResourceExhausted</c>
///      arrivent tous en « 200 OK » au gestionnaire de messages. Un
///      <c>ShouldHandle</c> écrit sur <c>HttpResponseMessage.StatusCode</c> —
///      c'est-à-dire le réflexe — ne compte donc aucune de ces pannes.
///
///   2. UN DÉPASSEMENT D'ÉCHÉANCE Y EST INDISCERNABLE D'UNE ANNULATION PAR
///      L'APPELANT. Les deux arrivent en <c>OperationCanceledException</c> sur le
///      même jeton lié. Compter les deux ferait ouvrir le disjoncteur quand un
///      utilisateur ferme son onglet ; n'en compter aucune laisserait le cas le
///      plus fréquent — le service LENT — hors du compte.
///
/// Ici, au niveau gRPC, <see cref="RpcException.StatusCode"/> est explicite et
/// <c>DeadlineExceeded</c> ne se confond avec rien.
///
/// ET POURQUOI L'ÉTAT EST CONSULTÉ AVANT D'APPELER `continuation`.
///
/// Un disjoncteur qui laisse partir l'appel avant de constater qu'il est ouvert
/// ne protège personne : le service en panne reçoit quand même la requête. Or
/// <c>ExecuteAsync</c> ne peut vérifier l'état qu'à l'entrée du délégué — et le
/// délégué, ici, ne peut qu'ATTENDRE un appel déjà démarré, puisqu'un
/// intercepteur doit rendre son <see cref="AsyncUnaryCall{TResponse}"/>
/// SYNCHRONEMENT. D'où la consultation explicite de
/// <see cref="CircuitBreakerStateProvider"/> avant de composer le numéro.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>PAS DE RÉESSAI, ET CE N'EST PAS UN OUBLI.</b> En HTTP, la passerelle ne
/// réessaie que les GET et HEAD : rejouer un POST dont la réponse s'est perdue
/// débite deux fois. En gRPC, RIEN ne dit si un RPC est sûr — <c>GetSellerPayout</c>
/// et <c>RefundPayment</c> ont la même forme. Un réessai générique rembourserait
/// donc deux fois. Le jour où l'on en voudra un, il devra être déclaré RPC par
/// RPC, pas déduit.
/// </para>
///
/// <para>
/// <b>UN DISJONCTEUR PAR SERVICE APPELÉ, PAS UN POUR TOUT LE MONDE.</b> La clé
/// est <c>Method.ServiceName</c>. Un seller-service en panne ne doit pas couper
/// les appels au catalogue : un disjoncteur global transformerait une panne en
/// panne générale, ce qu'il est censé empêcher.
/// </para>
///
/// <para>
/// <b>CE QU'IL NE COUVRE PAS.</b> Seuls les appels UNAIRES passent par ici —
/// c'est-à-dire tous ceux du dépôt aujourd'hui. Un futur RPC en FLUX ne serait ni
/// disjoncté, ni doté d'une échéance, ni porteur de la clé interne : la même
/// limite est déjà écrite pour <c>InternalCallClientInterceptor</c>, et elle a la
/// même cause — seul <c>AsyncUnaryCall</c> est surchargé.
/// </para>
/// </remarks>
public sealed class DisjoncteurClientInterceptor : Interceptor
{
    /// <summary>
    /// Part d'échecs à partir de laquelle on coupe, sur la fenêtre d'observation.
    ///
    /// ACCOMPAGNÉE D'UN SEUIL DE VOLUME (<see cref="AppelsMinimum"/>) : sans
    /// lui, DEUX appels dont un échoue donneraient 50 % et couperaient un service
    /// parfaitement sain au premier hoquet d'un service peu sollicité.
    /// </summary>
    public const double PartDEchecs = 0.5;

    /// <summary>Fenêtre glissante d'observation.</summary>
    public static readonly TimeSpan FenetreDObservation = TimeSpan.FromSeconds(30);

    /// <summary>Nombre d'appels en dessous duquel on ne conclut rien.</summary>
    public const int AppelsMinimum = 10;

    /// <summary>
    /// Durée de coupure.
    ///
    /// PLUS LONGUE QUE L'ÉCHÉANCE DE 5 s, ET IL LE FAUT : couper moins
    /// longtemps qu'un appel ne dure ferait rouvrir sur un service encore occupé
    /// à traiter la vague précédente.
    /// </summary>
    public static readonly TimeSpan DureeDeCoupure = TimeSpan.FromSeconds(15);

    private readonly ILogger<DisjoncteurClientInterceptor> _journal;

    private readonly ConcurrentDictionary<string, Disjoncteur> _parService = new(StringComparer.Ordinal);

    public DisjoncteurClientInterceptor(ILogger<DisjoncteurClientInterceptor> journal)
        => _journal = journal;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var service = context.Method.ServiceName;
        var disjoncteur = _parService.GetOrAdd(service, Construire);

        if (disjoncteur.Etat.CircuitState is CircuitState.Open or CircuitState.Isolated)
        {
            return Coupe<TResponse>(service, context.Method.Name);
        }

        var appel = continuation(request, context);

        // L'APPEL EST DÉJÀ PARTI ; LE PIPELINE N'ENCADRE QUE SON ATTENTE.
        //
        // C'est ce qui permet au disjoncteur de COMPTER les issues. La coupure,
        // elle, a lieu plus haut, avant `continuation`. Si l'état bascule entre
        // les deux — course inévitable —, `ExecuteAsync` lève
        // `BrokenCircuitException`, traduite ci-dessous comme une coupure : on
        // aura payé un appel de trop, jamais une exception non gRPC remontée au
        // code métier.
        var reponse = Attendre(disjoncteur, appel, service, context.Method.Name);

        return new AsyncUnaryCall<TResponse>(
            reponse,
            appel.ResponseHeadersAsync,
            appel.GetStatus,
            appel.GetTrailers,
            appel.Dispose);
    }

    private async Task<TResponse> Attendre<TResponse>(
        Disjoncteur disjoncteur, AsyncUnaryCall<TResponse> appel, string service, string rpc)
    {
        try
        {
            return await disjoncteur.Pipeline
                .ExecuteAsync(async _ => await appel.ResponseAsync.ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        catch (BrokenCircuitException)
        {
            _journal.LogWarning(
                "Disjoncteur ouvert pour {Service} : {Rpc} refusé sans appel.", service, rpc);

            throw new RpcException(StatutDeCoupure(service));
        }
    }

    /// <summary>
    /// L'appel refusé sans avoir été émis.
    /// </summary>
    /// <remarks>
    /// LE STATUT EST `Unavailable`, ET C'EST LE SEUL HONNÊTE.
    ///
    /// L'appelant doit pouvoir distinguer « le service refuse » de « le service
    /// est injoignable » : ici, on ne sait RIEN de la demande, on n'a même pas
    /// composé le numéro. `Unavailable` dit « réessaie plus tard », ce qui est
    /// exactement la situation — et c'est déjà le statut que reçoit un appelant
    /// quand la connexion est refusée, donc rien de nouveau à gérer chez lui.
    /// </remarks>
    private AsyncUnaryCall<TResponse> Coupe<TResponse>(string service, string rpc)
    {
        _journal.LogWarning(
            "Disjoncteur ouvert pour {Service} : {Rpc} refusé sans appel.", service, rpc);

        var statut = StatutDeCoupure(service);

        return new AsyncUnaryCall<TResponse>(
            Task.FromException<TResponse>(new RpcException(statut)),
            Task.FromResult(new Metadata()),
            () => statut,
            () => new Metadata(),
            () => { });
    }

    private static Status StatutDeCoupure(string service)
        => new(StatusCode.Unavailable,
            $"Disjoncteur ouvert : {service} est considéré en panne, l'appel n'a pas été émis.");

    private Disjoncteur Construire(string service)
    {
        var etat = new CircuitBreakerStateProvider();

        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = PartDEchecs,
                SamplingDuration = FenetreDObservation,
                MinimumThroughput = AppelsMinimum,
                BreakDuration = DureeDeCoupure,
                StateProvider = etat,
                ShouldHandle = arguments => ValueTask.FromResult(EstUnePanne(arguments.Outcome.Exception))
            })
            .Build();

        return new Disjoncteur(pipeline, etat);
    }

    /// <summary>
    /// Ce qui compte comme une panne du service appelé.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// UN REFUS MÉTIER N'EST PAS UNE PANNE, ET LES CONFONDRE OUVRIRAIT LE
    /// DISJONCTEUR SUR UN SERVICE EN PARFAIT ÉTAT.
    ///
    /// `InvalidArgument` (un GUID malformé), `NotFound`, `FailedPrecondition`,
    /// `PermissionDenied`, `AlreadyExists` : le service a répondu, vite et
    /// correctement. Une campagne de requêtes fautives — un client qui boucle sur
    /// un identifiant invalide — couperait sinon l'accès pour tout le monde.
    ///
    /// `Unknown` EST COMPTÉ, ET C'EST DÉLIBÉRÉ.
    ///
    /// C'est ce que rend une exception non gérée côté serveur, `EnableDetailedErrors`
    /// étant à faux : une panne de base de données arrive sous ce statut. L'y
    /// exclure laisserait la panne la plus lourde hors du compte. Le prix : un bug
    /// de sérialisation répété ouvre aussi le disjoncteur — ce qui n'est pas faux
    /// non plus, le service ne rendant alors rien d'exploitable.
    ///
    /// `Cancelled` EST EXCLU : c'est l'appelant qui a renoncé, pas le service.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    /// <remarks>
    /// PUBLIQUE POUR ÊTRE TESTÉE, ET C'EST LE SEUL MEMBRE QUI L'EST.
    ///
    /// Le reste de la classe se teste mal — il faudrait un canal gRPC. Cette
    /// décision-ci, en revanche, EST le disjoncteur : se tromper de liste, c'est
    /// soit couper sur des refus métier (donc rendre un service sain
    /// inaccessible), soit ne jamais couper. Une méthode privée non testée aurait
    /// été le meilleur endroit pour cacher cette erreur-là.
    /// </remarks>
    public static bool EstUnePanne(Exception? exception)
        => exception is RpcException rpc
            && rpc.StatusCode is StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.ResourceExhausted
                or StatusCode.Internal
                or StatusCode.DataLoss
                or StatusCode.Unknown;

    private sealed record Disjoncteur(ResiliencePipeline Pipeline, CircuitBreakerStateProvider Etat);
}
