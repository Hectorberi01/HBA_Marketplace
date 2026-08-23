using HBA.Gateway.Api.Middlewares;
using HBA.Gateway.Api.Options;
using HBA.Identity.Contracts.Grpc;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;

namespace HBA.Gateway.Api.Extensions;

/// <summary>
/// Câblage du contrôle de révocation (ISSUE-022, décision D27).
/// </summary>
public static class TokenRevocationExtensions
{
    /// <param name="estDeveloppement">
    /// TRANSMIS PLUTÔT QUE DEVINÉ.
    ///
    /// La passerelle n'appelle pas `AddHbaGrpc` — elle n'est pas un serveur gRPC —
    /// donc le refus du mode d'identité non signé hors développement n'y arrive
    /// pas tout seul. Le résoudre ici depuis `IConfiguration` reviendrait à lire
    /// `ASPNETCORE_ENVIRONMENT` à la main, c'est-à-dire à réécrire
    /// `IHostEnvironment.IsDevelopment()` avec une chance de le faire autrement.
    /// </param>
    public static IServiceCollection AddGatewayTokenRevocation(
        this IServiceCollection services, IConfiguration configuration, bool estDeveloppement)
    {
        IdentiteInterne.RefuserLeModeNonSigneHorsDeveloppement(
            configuration.GetValue<bool>(
                $"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.IdentitesNonSignees)}"),
            estDeveloppement);

        services.Configure<TokenRevocationOptions>(
            configuration.GetSection(TokenRevocationOptions.SectionName));

        // ENREGISTRÉ MÊME QUAND LE CONTRÔLE EST DÉSACTIVÉ, ET C'EST VOLONTAIRE.
        //
        // `Enabled` court-circuite dans le middleware, pas ici. Conditionner
        // l'enregistrement ferait dépendre la composition d'un drapeau : le
        // middleware exigerait alors un `IMemoryCache` absent, et l'erreur
        // n'arriverait qu'à la PREMIÈRE REQUÊTE, en pleine exécution — pas au
        // démarrage, où on la lit.
        services.AddMemoryCache();

        // ═════════════════════════════════════════════════════════════════════
        // LA PASSERELLE N'APPELLE PAS `AddHbaService` : CE QUE LE SOCLE POSE
        // AILLEURS, IL FAUT LE POSER ICI.
        //
        // `AddIdentityGrpcClient` attache `InternalCallClientInterceptor`, qui a
        // besoin de deux choses : la clé partagée liée depuis la section
        // `Internal`, et `IHttpContextAccessor` pour recopier l'identifiant de
        // corrélation dans les métadonnées gRPC.
        //
        // Sans la première, l'intercepteur n'envoie PAS d'en-tête et
        // `InternalCallServerInterceptor` refuse l'appel côté identity — en
        // `Unavailable : Internal API not configured`, après un aller-retour HTTP
        // réussi. Cela envoie chercher un problème de réseau là où il n'y en a pas.
        //
        // Sans le second, l'injection du singleton échoue à la construction du
        // client. C'est le bon échec, mais autant ne pas le provoquer.
        //
        // `CorrelationIdMiddleware` de la passerelle et
        // `ServiceCorrelationMiddleware` du socle écrivent sous LA MÊME clé
        // `X-Correlation-ID` dans `HttpContext.Items`. La corrélation traverse donc
        // bien ce saut gRPC. Renommer l'une des deux la couperait en silence.
        // ═════════════════════════════════════════════════════════════════════
        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddSingleton<InternalCallClientInterceptor>();

        // MANQUANT DEPUIS LE LOT 8.8, ET L'ERREUR N'ARRIVAIT QU'À L'EXÉCUTION.
        //
        // `AjouterLesInterceptionsInternes` — que `AddIdentityGrpcClient` appelle
        // deux lignes plus bas — pose DEUX intercepteurs, et `AddInterceptor<T>`
        // les résout depuis le conteneur au moment où le client est FABRIQUÉ. La
        // passerelle n'enregistrait que le premier : le contrôle de révocation
        // aurait levé `InvalidOperationException` à la première vérification de
        // jeton, donc à la première requête authentifiée, et non au démarrage.
        //
        // Les vingt-trois services ne l'ont pas parce que `AddHbaGrpc` les
        // enregistre tous les deux ; la passerelle n'appelle pas `AddHbaGrpc`.
        services.AddSingleton<DisjoncteurClientInterceptor>();

        // LÈVE À LA CONSTRUCTION DE L'HÔTE si `Services:Identity` est absente.
        // C'est déjà le cas sans ce fichier : `ServicesOptions.Identity` porte
        // `[Required, Url]` et la validation est vérifiée au démarrage.
        services.AddIdentityGrpcClient(configuration);

        return services;
    }

    /// <summary>
    /// À PLACER APRÈS `UseAuthentication` ET APRÈS `UseRateLimiter`.
    ///
    /// Après l'authentification, parce que le middleware ne travaille que sur une
    /// requête déjà authentifiée — c'est ce qui garantit qu'un jeton mal signé
    /// n'atteint jamais identity, et donc que le cache ne peut pas être gonflé de
    /// l'extérieur.
    ///
    /// Après le limiteur, parce qu'une rafale doit être coupée AVANT de se
    /// transformer en rafale d'appels gRPC vers identity. L'inverse ferait du
    /// contrôle de révocation un amplificateur de charge.
    ///
    /// Avant l'autorisation, parce qu'un jeton mort ne doit pas franchir la moindre
    /// politique : le refuser après reviendrait à laisser une session révoquée être
    /// évaluée comme si elle vivait.
    /// </summary>
    public static WebApplication UseGatewayTokenRevocation(this WebApplication app)
    {
        app.UseMiddleware<TokenRevocationMiddleware>();

        return app;
    }
}
