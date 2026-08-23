using System.Security.Cryptography;
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
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// ISSUE-022, MISE EN ŒUVRE DE LA DÉCISION D27.
///
/// POURQUOI ICI, ET PAS DANS `AddHbaService`.
///
/// Mettre le contrôle dans le socle partagé aurait semblé plus rigoureux — chaque
/// service se défend lui-même. Il aurait fallu `Services:Identity` dans quatorze
/// configurations, un client gRPC dans quatorze hôtes, et surtout : identity
/// serait devenue une dépendance dure de CHAQUE requête de la plateforme. Une
/// latence sur identity serait devenue une latence sur tout.
///
/// Tout le trafic externe passe par cette passerelle. Les appels de service à
/// service, eux, ne portent aucun jeton d'utilisateur : leur garde est
/// l'intercepteur à clé partagée. Le seul endroit où un jeton révoqué peut entrer
/// est donc ici — un point de contrôle, un cache, un client.
///
/// COROLLAIRE À TENIR : le jour où un service devient joignable hors de la
/// passerelle, ce raisonnement tombe. C'est une contrainte de DÉPLOIEMENT, pas une
/// opinion — la même que celle qui gouverne `OUTBOX_ENABLED`.
///
/// L'ÉCHEC EST OUVERT, ET IL EST BRUYANT.
///
/// Le dépôt refuse de démarrer plutôt que de simuler, et c'est la bonne règle au
/// démarrage : une plateforme qui ne boote pas se répare en cinq minutes. Elle ne
/// s'applique pas ici. Fermer signifierait qu'une panne d'identity rende 401 à
/// tout le monde, paiements en cours compris : l'indisponibilité d'un service
/// deviendrait l'indisponibilité de la plateforme.
///
/// Ouvert, un compte suspendu conserve ses droits PENDANT la panne, borné par la
/// durée de vie du jeton — c'est-à-dire exactement le risque subi en permanence
/// avant ce fichier, mais réduit aux minutes d'une panne. Le journal est donc
/// `Critical`, pas `Warning` : un contrôle de sécurité désactivé en silence est
/// pire que son absence, parce que personne ne le sait.
///
/// LE CACHE EST INDEXÉ PAR EMPREINTE, JAMAIS PAR LE JETON.
///
/// Une clé de cache se retrouve dans un vidage mémoire, dans une trace de
/// diagnostic, parfois dans un compteur de métrique. Y mettre le jeton en clair
/// reviendrait à recréer la fuite que le chiffrement de l'outbox vient de fermer,
/// en plus discret.
///
/// CE CACHE NE PEUT PAS ÊTRE GONFLÉ PAR UN ATTAQUANT, et c'est une propriété,
/// pas une chance : ce middleware ne s'exécute qu'APRÈS `UseAuthentication`. Un
/// jeton mal signé n'arrive jamais jusqu'ici. Le nombre d'entrées est donc borné
/// par les sessions réellement émises par identity.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class TokenRevocationMiddleware
{
    private const string PrefixeDeCle = "revocation:";

    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly TokenRevocationOptions _options;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    public TokenRevocationMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IOptions<TokenRevocationOptions> options,
        ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _cache = cache;
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

        /// <summary>identity n'a pas répondu. On laisse passer, et on le crie.</summary>
        Inconnu
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Une requête anonyme n'a rien à révoquer. Cela couvre les sondes de
        // santé, la connexion, le rafraîchissement et la documentation.
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
        //
        // `AddIdentityGrpcClient` enregistre `IIdentityModuleApi` en Scoped, et ce
        // middleware est un singleton. L'injecter au constructeur lèverait au
        // démarrage — ou, pire, capturerait la toute première portée pour la durée
        // de vie du processus.
        var identity = context.RequestServices.GetRequiredService<IIdentityModuleApi>();

        using var delai = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        delai.CancelAfter(TimeSpan.FromMilliseconds(_options.TimeoutMilliseconds));

        Verdict verdict;
        TimeSpan duree;

        try
        {
            var validation = await identity.ValidateAccessTokenAsync(jeton, delai.Token);

            verdict = validation.Valid ? Verdict.Vivant : Verdict.Revoque;
            duree = TimeSpan.FromSeconds(_options.CacheSeconds);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !context.RequestAborted.IsCancellationRequested)
        {
            // `Critical`, ET LA RAISON EST DANS LE MESSAGE.
            //
            // Pendant tout le temps où cette ligne s'écrit, un compte suspendu ou
            // déconnecté conserve ses droits. C'est un choix assumé (D27), pas un
            // incident mineur : il doit réveiller quelqu'un, pas se noyer dans le
            // bruit d'un niveau `Warning`.
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
        _cache.Set(cle, verdict, duree);

        return verdict;
    }

    private async Task RefuserAsync(HttpContext context)
    {
        _logger.LogInformation(
            "Jeton révoqué refusé sur {Method} {Path}.",
            context.Request.Method, context.Request.Path);

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Dit au client que le jeton est mort et qu'il doit en redemander un. Sans
        // cet en-tête, une application mobile ne distingue pas ce 401 d'un droit
        // manquant, et boucle sur une requête qui ne passera plus jamais.
        context.Response.Headers.WWWAuthenticate =
            "Bearer error=\"invalid_token\", error_description=\"The access token has been revoked\"";

        var problem = new ProblemDetails
        {
            Type = "https://api.hba-express.com/errors/token-revoked",
            Title = "Unauthorized",
            Status = StatusCodes.Status401Unauthorized,

            // AUCUN DÉTAIL SUR LA CAUSE. Distinguer « compte suspendu » de
            // « mot de passe changé » renseignerait quiconque détient un jeton volé
            // sur ce que le propriétaire légitime vient de faire.
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

    /// <summary>
    /// Empreinte SHA-256 du jeton, en base64url.
    ///
    /// CE N'EST PAS DU CHIFFREMENT, ET CE N'EST PAS CE QU'ON LUI DEMANDE. Le
    /// besoin est qu'une clé de cache ne permette pas de reconstituer le jeton, y
    /// compris pour qui lit un vidage mémoire. Une fonction à sens unique suffit.
    /// </summary>
    private static string Empreinte(string jeton)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jeton)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
