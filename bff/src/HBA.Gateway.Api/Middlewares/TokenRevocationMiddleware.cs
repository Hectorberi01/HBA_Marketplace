using System.Security.Cryptography;
using HBA.Gateway.Infrastructure.Messaging.Kafka;
using System.Text;
using HBA.Gateway.Api.Extensions;
using HBA.Gateway.Api.Options;
using HBA.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace HBA.Gateway.Api.Middlewares;

/// <summary>
/// Refuse un jeton révoqué — déconnexion, changement de mot de passe, suspension.
/// </summary>
public sealed class TokenRevocationMiddleware
{
    private const string PrefixeDeCle = "revocation:";

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly RegistreDeRevocation _registre;
    private readonly TokenRevocationOptions _options;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    public TokenRevocationMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        RegistreDeRevocation registre,
        IOptions<TokenRevocationOptions> options,
        ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _registre = registre;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Ce que l'on sait du jeton présenté.</summary>
    private enum Verdict
    {
        /// <summary>identity a répondu : le jeton vit.</summary>
        Vivant,

        /// <summary>identity a répondu : le jeton est mort.</summary>
        Revoque,

        /// <summary>identity n'a pas répondu.</summary>
        Inconnu
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Une requête anonyme n'a rien à révoquer.
        if (!_options.Enabled || context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var jeton = LireJetonPorteur(context.Request);

        // Authentifié sans jeton porteur lisible : on ne sait pas quoi valider, et
        // inventer une valeur ferait refuser des requêtes légitimes.
        if (jeton is null)
        {
            await _next(context);
            return;
        }

        var verdict = await ObtenirVerdictAsync(jeton, context);

        if (verdict == Verdict.Revoque)
        {
            await RefuserAsync(context);
            return;
        }

        await _next(context);
    }

    private async Task<Verdict> ObtenirVerdictAsync(string jeton, HttpContext context)
    {
        var cle = PrefixeDeCle + Empreinte(jeton);

        if (_cache.TryGetValue<Verdict>(cle, out var memorise))
        {
            return memorise;
        }

        // RÉSOLU PAR REQUÊTE, PAS INJECTÉ AU CONSTRUCTEUR.
        var identity = context.RequestServices.GetRequiredService<IIdentityModuleApi>();

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        delai.CancelAfter(TimeSpan.FromMilliseconds(_options.TimeoutMilliseconds));

        Verdict verdict;
        TimeSpan duree;

        // LE COMPTE, POUR POUVOIR EVINCER SANS CONNAITRE LES JETONS.
        Guid? compte = null;

        try
        {
            var validation = await identity.ValidateAccessTokenAsync(jeton, delai.Token);

            verdict = validation.Valid ? Verdict.Vivant : Verdict.Revoque;
            duree = TimeSpan.FromSeconds(_options.CacheSeconds);
            compte = validation.UserId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !context.RequestAborted.IsCancellationRequested)
        {
            // `Critical`, ET LA RAISON EST DANS LE MESSAGE.
            _logger.LogCritical(
                exception,
                "CONTRÔLE DE RÉVOCATION HORS SERVICE : identity-service est injoignable. "
                + "Les jetons révoqués — déconnexion, changement de mot de passe, suspension — "
                + "restent acceptés jusqu'à leur expiration naturelle. Requête laissée passer "
                + "sur {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            verdict = Verdict.Inconnu;
            duree = TimeSpan.FromSeconds(_options.FailOpenCacheSeconds);
        }

        // Expiration ABSOLUE, jamais glissante : une session active repousserait
        // indéfiniment sa propre vérification, et la révocation ne mordrait que sur
        // les comptes inactifs — c'est-à-dire jamais sur celui qu'on veut couper.
        var entree = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = duree };

        // L'ENTREE EST ATTACHEE AU COMPTE, ET C'EST CE QUI REND LA COUPURE
        // IMMEDIATE POSSIBLE.
        if (compte is { } utilisateur && utilisateur != Guid.Empty)
        {
            entree.AddExpirationToken(_registre.JetonDExpiration(utilisateur));
        }

        _cache.Set(cle, verdict, entree);

        return verdict;
    }

    private async Task RefuserAsync(HttpContext context)
    {
        _logger.LogInformation(
            "Jeton révoqué refusé sur {Method} {Path}.",
            context.Request.Method, context.Request.Path);

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Dit au client que le jeton est mort et qu'il doit en redemander un.
        context.Response.Headers.WWWAuthenticate =
            "Bearer error=\"invalid_token\", error_description=\"The access token has been revoked\"";

        var problem = new ProblemDetails
        {
            Type = "https://api.hba-express.com/errors/token-revoked",
            Title = "Unauthorized",
            Status = StatusCodes.Status401Unauthorized,

            // AUCUN DÉTAIL SUR LA CAUSE. Distinguer « compte suspendu » de « mot de
            // passe changé » renseignerait quiconque détient un jeton volé sur ce
            // que le propriétaire légitime vient de faire.
            Detail = "La session n'est plus valide. Reconnectez-vous.",
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: RateLimitingExtensions.ProblemJson,
            context.RequestAborted);
    }

    /// <summary>Le jeton porteur brut, tel qu'identity devra le relire.</summary>
    private static string? LireJetonPorteur(HttpRequest request)
    {
        var brut = request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(brut))
        {
            return null;
        }

        const string schema = "Bearer ";

        if (!brut.StartsWith(schema, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var jeton = brut[schema.Length..].Trim();

        return jeton.Length == 0 ? null : jeton;
    }

    /// <summary>Empreinte SHA-256 du jeton, en base64url.</summary>
    private static string Empreinte(string jeton)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jeton)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
