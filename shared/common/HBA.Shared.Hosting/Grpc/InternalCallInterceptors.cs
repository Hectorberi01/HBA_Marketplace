using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Options;

namespace HBA.Shared.Hosting.Grpc;

/// <summary>
/// Côté CLIENT : joint le secret interne et l'identifiant de corrélation à chaque
/// appel sortant.
/// </summary>
public sealed class InternalCallClientInterceptor : Interceptor
{
    private readonly IOptions<InternalCallOptions> _options;
    private readonly IOptions<EcheancesGrpcOptions> _echeances;
    private readonly IHttpContextAccessor _accessor;

    public InternalCallClientInterceptor(
        IOptions<InternalCallOptions> options,
        IOptions<EcheancesGrpcOptions> echeances,
        IHttpContextAccessor accessor)
    {
        _options = options;
        _echeances = echeances;
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

        // L'ATTESTATION D'IDENTITÉ EST FRAPPÉE ICI, DONC UNE FOIS PAR APPEL.
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
            headers.Add(IdentiteInterne.MetadataKey, NomDeCetHote());
        }
        else
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "Internal identity not configured."));
        }

        // LA CORRÉLATION DOIT TRAVERSER LE SAUT gRPC.
        var correlationId = _accessor.HttpContext?
            .Items[ServiceCorrelationMiddleware.HeaderName]?.ToString();

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            headers.Add("x-correlation-id", correlationId);
        }

        var options = context.Options.WithHeaders(headers);

        // UN APPEL gRPC SANS ÉCHÉANCE ATTEND INDÉFINIMENT.
        if (options.Deadline is null)
        {
            options = options.WithDeadline(
                DateTime.UtcNow + _echeances.Value.Pour(context.Method.ServiceName));
        }

        return continuation(request, new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, options));
    }

    /// <summary>
    /// Nom d'identité de cet hôte : `Internal:ServiceName`, sinon l'assembly
    /// d'entrée.
    /// </summary>
    private string NomDeCetHote()
        => _nom ??= string.IsNullOrWhiteSpace(_options.Value.ServiceName)
            ? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "?"
            : _options.Value.ServiceName!;

    private string? _nom;
}

/// <summary>Côté SERVEUR : refuse tout appel qui ne présente pas le secret interne.</summary>
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
            // Clé absente = configuration incomplète.
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition, "Internal API not configured."));
        }

        var presented = context.RequestHeaders.GetValue(InternalRoutes.MetadataKey);

        if (!InternalRoutes.SecretsMatch(presented, expected))
        {
            // `Unauthenticated`, ET NON `NotFound` : voir l'encadré de la classe.
            throw new RpcException(new Status(
                StatusCode.Unauthenticated, "Internal call rejected."));
        }

        // DEUXIÈME SERRURE : QUI APPELLE, ET A-T-IL LE DROIT.
        var registre = _registre ??= IdentiteInterne.LireRegistre(_options.Value.PublicKeys);

        var presentee = context.RequestHeaders.GetValue(IdentiteInterne.MetadataKey);

        // CHEMIN DE DÉVELOPPEMENT — VOIR `InternalCallOptions.IdentitesNonSignees`.
        if (_options.Value.IdentitesNonSignees
            && !string.IsNullOrWhiteSpace(presentee)
            && !presentee.Contains('.'))
        {
            return Autoriser(presentee, request, context, continuation);
        }

        if (registre.Count == 0)
        {
            // Même raisonnement que pour la clé absente ci-dessus : registre vide =
            // configuration incomplète, erreur PERMANENTE, donc
            // `FailedPrecondition` — que le disjoncteur des appelants ne compte pas
            // comme une panne.
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

    /// <summary>Applique la table d'autorisations à un appelant DÉJÀ identifié.</summary>
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
            throw new RpcException(new Status(
                StatusCode.PermissionDenied, "Internal call not permitted."));
        }

        // Le nom de l'appelant est déposé pour les couches au-dessus — journal,
        // trace, futur audit des appels internes.
        context.UserState["appelant-interne"] = appelant;

        return continuation(request, context);
    }
}
