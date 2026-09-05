using HBA.Shared.Hosting.Grpc;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace HBA.Shared.Hosting;

/// <summary>Ports et protocoles d'un service HBA.</summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>REST/JSON, appelé par la passerelle. HTTP/1.1.</summary>
    public int HttpPort { get; init; } = 8080;

    /// <summary>gRPC, appelé par les autres services. HTTP/2 en clair.</summary>
    public int GrpcPort { get; init; } = 8081;
}

public static class GrpcHostExtensions
{
    /// <summary>
    /// Ouvre deux ports : REST sur l'un, gRPC sur l'autre.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// DEUX PORTS, PARCE QU'IL N'Y A PAS DE TLS SUR LE RÉSEAU INTERNE.
    ///
    /// Sur un même port, un serveur distingue HTTP/1.1 de HTTP/2 grâce à ALPN,
    /// qui fait partie de la poignée de main TLS. En clair, ALPN n'existe pas :
    /// Kestrel devrait deviner, et ne le peut qu'en imposant au client d'envoyer
    /// le préambule HTTP/2 d'emblée. Cela fonctionne avec le client gRPC .NET
    /// correctement configuré, et échoue avec tout le reste — `curl`, une sonde,
    /// un futur client d'un autre langage.
    ///
    /// Deux ports rendent le protocole explicite, et permettent en prime une
    /// politique réseau distincte : 8080 joignable depuis `hba-proxy`, 8081
    /// depuis `hba-backend` seulement.
    ///
    /// APPELER `ListenAnyIP` FAIT IGNORER `ASPNETCORE_URLS`, ENTIÈREMENT.
    ///
    /// Ce n'est pas une fusion des deux configurations : dès qu'un `Listen*`
    /// explicite existe, la variable d'environnement est écartée — avec, au
    /// mieux, un avertissement dans le journal. Les fichiers `env/*.env` la
    /// définissaient ; elle y a été remplacée par `Hosting__HttpPort` pour que
    /// personne ne passe une heure à modifier une variable sans effet.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static WebApplicationBuilder AddHbaGrpc(this WebApplicationBuilder builder)
    {
        // AVANT TOUT LE RESTE : UN HÔTE MAL CONFIGURÉ NE DOIT PAS SE CONSTRUIRE.
        //
        // Voir `IdentiteInterne.RefuserLeModeNonSigneHorsDeveloppement`. Le
        // contrôle est ici plutôt que dans un service de démarrage parce qu'un
        // `IHostedService` s'exécute APRÈS que Kestrel a ouvert ses ports : le
        // service accepterait des appels pendant la fraction de seconde qui
        // précède son propre arrêt.
        IdentiteInterne.RefuserLeModeNonSigneHorsDeveloppement(
            builder.Configuration.GetValue<bool>(
                $"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.IdentitesNonSignees)}"),
            builder.Environment.IsDevelopment());

        // LA CLÉ PRIVÉE EST LUE ICI, ET NON AU PREMIER APPEL gRPC.
        //
        // `Signer` la décode dans un `GetOrAdd` : mal formée, elle laissait
        // l'hôte démarrer et n'échouait qu'au premier appel sortant, en 500
        // opaque sur la route appelante. Voir
        // `IdentiteInterne.RefuserUneClePriveeIllisible`.
        IdentiteInterne.RefuserUneClePriveeIllisible(
            builder.Configuration[$"{InternalCallOptions.SectionName}:{nameof(InternalCallOptions.PrivateKey)}"]);

        var hosting = builder.Configuration
            .GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>() ?? new HostingOptions();

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(hosting.HttpPort, listen => listen.Protocols = HttpProtocols.Http1);
            kestrel.ListenAnyIP(hosting.GrpcPort, listen => listen.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddSingleton<TraductionDesErreursServerInterceptor>();
        builder.Services.AddSingleton<InternalCallServerInterceptor>();
        builder.Services.AddSingleton<InternalCallClientInterceptor>();

        // SINGLETON, ET C'EST LA CONDITION POUR QU'IL SERVE À QUELQUE CHOSE.
        //
        // Un disjoncteur EST un état : le compte d'échecs sur la fenêtre glissante
        // et l'instant de réouverture. Enregistré en `Scoped`, il serait reconstruit
        // à chaque requête, repartirait de zéro à chaque fois, et ne compterait
        // jamais jusqu'à dix. Il ne casserait rien — il ne couperait simplement
        // jamais, en donnant toutes les apparences d'être en place.
        builder.Services.AddSingleton<DisjoncteurClientInterceptor>();

        builder.Services.AddGrpc(options =>
        {
            // LA TRADUCTION EST POSÉE EN PREMIER, DONC LA PLUS À L'EXTÉRIEUR.
            //
            // C'est la condition pour qu'elle voie ce que lève le handler — et
            // aussi ce que lève l'intercepteur de clé interne, qu'elle laisse
            // passer intact puisqu'il s'agit déjà d'une `RpcException`. Posée en
            // second, elle n'attraperait rien de ce qui vient d'au-dessus d'elle.
            options.Interceptors.Add<TraductionDesErreursServerInterceptor>();
            options.Interceptors.Add<InternalCallServerInterceptor>();

            // LES DÉTAILS D'EXCEPTION NE SORTENT PAS, MÊME EN DÉVELOPPEMENT.
            //
            // `EnableDetailedErrors` renvoie le message et la pile de l'exception
            // au client. Sur ce réseau, le « client » est un autre service, qui
            // journalise ce qu'il reçoit — une chaîne de connexion PostgreSQL
            // partie d'ici finirait dans les journaux d'un service voisin, et
            // personne ne saurait qu'elle y est.
            options.EnableDetailedErrors = false;
        });

        return builder;
    }

    /// <summary>
    /// Publie un service gRPC interne, explicitement hors du pipeline
    /// d'autorisation HTTP.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI `AllowAnonymous` SUR UNE ROUTE INTERNE — ET POURQUOI CE N'EST
    /// PAS UN TROU.
    ///
    /// Un appel gRPC de service à service ne porte AUCUN jeton d'utilisateur :
    /// <see cref="Grpc.InternalCallClientInterceptor"/> n'envoie que la clé
    /// partagée `x-internal-key` et l'identifiant de corrélation. Sa
    /// contrepartie <see cref="Grpc.InternalCallServerInterceptor"/> rejette
    /// tout appel qui ne présente pas la clé — c'est LÀ qu'est la serrure.
    ///
    /// Depuis que le socle installe une FallbackPolicy (voir
    /// ServiceHostExtensions), un point de terminaison sans métadonnée
    /// d'autorisation exige un utilisateur authentifié. Appliquée à gRPC, elle
    /// répondrait 401 à TOUS les appels internes — et la panne ne se verrait
    /// qu'à l'exécution, service par service, sous la forme d'un catalogue
    /// « indisponible » sans cause apparente.
    ///
    /// D'où cette méthode plutôt que dix `.AllowAnonymous()` recopiés : la
    /// dispense est nommée, la raison tient en un seul endroit, et personne
    /// n'aura à la redécouvrir devant un `MapGrpcService` nu.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    public static GrpcServiceEndpointConventionBuilder MapInternalGrpcService<TService>(
        this IEndpointRouteBuilder app)
        where TService : class
    {
        var route = app.MapGrpcService<TService>();
        route.AllowAnonymous();
        return route;
    }
}
