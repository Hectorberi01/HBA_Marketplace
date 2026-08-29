using System.Text;
using HBA.Shared.Infrastructure.Hosting;
using HBA.Shared.Application;
using MediatR;
using HBA.Shared.Infrastructure;
using HBA.Shared.Hosting.Http;
using HBA.Shared.Hosting.OpenApi;
using HBA.Shared.Hosting.Telemetry;
using HBA.Shared.Infrastructure.Modularity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace HBA.Shared.Hosting;

/// <summary>
/// Amorçage commun aux treize services : configuration, module, authentification,
/// corrélation, erreurs, sondes.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE PROJET EXISTE PLUTÔT QUE TREIZE `Program.cs` COMPLETS.
///
/// Le démarrage d'un service — validation de jeton, corrélation, ProblemDetails,
/// sondes, outbox — représente environ deux cents lignes identiques. Recopiées
/// treize fois, elles divergent : un service oublie `ValidateLifetime`, un autre
/// laisse fuiter un message d'exception, un troisième expose `/health` sans
/// `AllowAnonymous` et redémarre en boucle. Ces écarts ne se voient jamais à la
/// lecture d'un service isolé.
///
/// CE PROJET NE CONTIENT AUCUNE RÈGLE MÉTIER, ET NE DOIT JAMAIS EN CONTENIR.
///
/// C'est la ligne à tenir : le socle partagé d'une plateforme de microservices
/// devient un monolithe distribué exactement le jour où l'on y met la première
/// règle « commune à tous les services ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class ServiceHostExtensions
{
    /// <summary>
    /// Enregistre le socle d'un service et son module métier.
    /// </summary>
    /// <typeparam name="TDbContext">DbContext du service, sondé par /health/ready.</typeparam>
    public static WebApplicationBuilder AddHbaService<TDbContext>(
        this WebApplicationBuilder builder, IModuleInstaller installer)
        where TDbContext : DbContext
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        // UNE SEULE ASSEMBLY SCANNÉE, ET C'EST TOUTE LA DIFFÉRENCE.
        //
        // Le monolithe passait ici les vingt-neuf assemblies de modules : un
        // handler de Catalog pouvait alors traiter un événement publié par
        // Ordering, dans le même processus. Un service n'en scanne qu'UNE — la
        // sienne. Ce qui traversait la frontière en mémoire doit désormais
        // passer par une route interne ou par Kafka, et le compilateur ne le
        // dira pas : c'est ce scan restreint qui le rend visible à l'exécution.
        services.AddMediatR(mediator =>
            mediator.RegisterServicesFromAssembly(installer.ApplicationAssembly));

        services.AddBuildingBlocksPipeline();

        // La configuration décide du cache distribué : Redis s'il est renseigné,
        // mémoire sinon — et le repli s'annonce au démarrage.
        services.AddBuildingBlocksInfrastructure(configuration);

        installer.Install(services, configuration);

        ConfigureForwardedHeaders(services);

        AddAuthentication(services, configuration);

        // ═════════════════════════════════════════════════════════════════════
        // LE FILET QUI MANQUAIT : SANS LUI, UN `MapGroup` NU EST ANONYME.
        //
        // `AddAuthorization()` seul n'installe AUCUNE politique de repli. Un
        // point de terminaison sans métadonnée d'autorisation est alors servi à
        // n'importe qui, sans jeton. Le commentaire d'en-tête d'ApiAuthorization
        // promettait l'inverse — mais la FallbackPolicy dont il parlait vivait
        // dans le Program.cs du MONOLITHE, et n'a jamais suivi l'extraction.
        //
        // Elle ne remplace pas les rôles : elle dit seulement « au moins un
        // compte ». Le jour où l'on écrira un groupe sans politique par
        // distraction, il sera fermé aux anonymes — pas aux acheteurs. C'est un
        // filet, pas une serrure.
        //
        // Ce qui doit rester ouvert est marqué `AllowAnonymous` explicitement :
        // les sondes de santé (voir UseHbaService), la vitrine catalogue et
        // food, les routes d'authentification, et les services gRPC (voir
        // MapInternalGrpcService — leur garde est l'interception à clé
        // partagée, pas le pipeline d'autorisation).
        // ═════════════════════════════════════════════════════════════════════
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        // SANS CET ENREGISTREMENT, `RequireRateLimiting("auth")` LÈVE AU DÉMARRAGE.
        //
        // Les politiques doivent exister avant que les routes ne les réclament.
        // L'erreur — « No policy found » — survient à la construction de l'hôte,
        // donc au démarrage du conteneur, et non à la première requête : c'est le
        // bon moment pour échouer, mais encore faut-il ne pas l'oublier.
        services.AddAuthRateLimiter();

        services
            .AddHealthChecks()
            .AddDbContextCheck<TDbContext>("database", tags: ["ready"]);

        // ═════════════════════════════════════════════════════════════════════
        // OPENAPI — LA SURFACE RÉELLE DE LA PLATEFORME N'ÉTAIT DOCUMENTÉE NULLE PART.
        //
        // La page de la passerelle ne montre que ses agrégations BFF : YARP inscrit
        // ses points de terminaison sans métadonnée d'exploration d'API, donc les
        // centaines de routes relayées n'y figurent pas et ne peuvent pas y figurer.
        // Écrire un client mobile supposait de lire `*Endpoints.cs`.
        //
        // L'assembly de la couche Application vient de l'installeur — c'est elle qui
        // porte les DTO, donc les descriptions de champs. Sans elle, les routes
        // seraient documentées et chaque schéma resterait nu.
        // ═════════════════════════════════════════════════════════════════════
        services.AddHbaOpenApi(
            FirstNonEmpty(configuration["SERVICE_NAME"], configuration["ServiceName"], "service"),
            installer.ApplicationAssembly);

        // ═════════════════════════════════════════════════════════════════════
        // INSTRUMENTATION — POSÉE ICI, DONC SUR LES QUATORZE SERVICES À LA FOIS.
        //
        // C'EST LE MÊME RAISONNEMENT QUE POUR LA `FallbackPolicy`, ET LA MÊME
        //    HISTOIRE.
        //
        // Un branchement à faire quatorze fois est un branchement qu'on oublie une
        // fois — et le service oublié est muet sans que rien ne le signale. Il
        // démarre, il sert, il passe ses tests. On ne s'en aperçoit qu'en cherchant
        // ses traces pendant un incident, c'est-à-dire au pire moment.
        //
        // Le nom de service est résolu ICI et non dans `UseHbaService` : la
        // ressource OpenTelemetry se fige à la construction du fournisseur, donc
        // avant que le pipeline ne soit monté. Les deux résolutions lisent la même
        // source (`SERVICE_NAME`), il n'y a pas deux vérités.
        // ═════════════════════════════════════════════════════════════════════
        builder.AddHbaTelemetry(FirstNonEmpty(
            configuration["SERVICE_NAME"],
            configuration["ServiceName"],
            "unknown-service"));

        return builder;
    }

    /// <summary>
    /// Pipeline commun. L'ordre est celui de la passerelle, pour la même raison.
    /// </summary>
    /// <param name="serviceName">
    /// Nom du service, ex. `user-service`. Omis, il est lu dans `SERVICE_NAME` — la
    /// variable que `docker-compose.dev.yml` pose déjà sur les quatorze services et
    /// que `KafkaEventNaming` utilise pour le champ `producer`. Une seule source.
    /// </param>
    /// <param name="serviceCode">
    /// Préfixe des codes `&lt;SERVICE&gt;_SERVICE_NOT_FOUND` du §10. Omis, il est déduit
    /// du nom du service, ce qui est JUSTE pour douze services sur seize et FAUX
    /// pour quatre : `commerce-service` doit rendre `MARKETPLACE_CART`,
    /// `order-service` `MARKETPLACE_ORDER`, `merchant-service` `MERCHANT` et
    /// `financial-service` `WALLET_AND_SETTLEMENT` ou `PAYMENT` selon l'agrégat.
    /// Ces quatre-là doivent le passer explicitement.
    /// </param>
    public static WebApplication UseHbaService(
        this WebApplication app, string? serviceName = null, string? serviceCode = null)
    {
        // AVANT TOUT LE RESTE : c'est lui qui remplace l'adresse de la passerelle
        // par celle du client. Placé après le limiteur de débit, il n'aurait plus
        // rien à corriger — la partition serait déjà calculée. Voir
        // ConfigureForwardedHeaders.
        if (!string.Equals(app.Configuration["ProxyTrust:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            app.UseForwardedHeaders();
        }

        // Intercepteur d'abord : il ne protège que ce qui le suit.
        app.UseMiddleware<ServiceExceptionMiddleware>();
        app.UseMiddleware<ServiceCorrelationMiddleware>();

        // ═════════════════════════════════════════════════════════════════════
        // LA DOCUMENTATION AVANT `UseAuthorization`, ET L'ORDRE EST LA RAISON
        //    POUR LAQUELLE ELLE FONCTIONNE.
        //
        // `AddHbaService` pose une politique de repli qui exige un compte
        // authentifié sur tout point de terminaison ne déclarant rien. Placée après
        // l'autorisation, la page répondrait 401 — avant même d'avoir pu servir le
        // bouton « Authorize » qui permet de s'authentifier. On tourne en rond, et
        // rien dans le message ne l'explique.
        //
        // Ici, le middleware Swagger court-circuite avant le routage : il sert son
        // document et rend la main. Ce n'est pas un contournement de la politique —
        // il n'expose que la SURFACE. Chaque route documentée continue d'appliquer
        // la sienne quand on l'appelle vraiment.
        //
        // Fermé hors Development sauf `OpenApi:Enabled=true` — voir `UseHbaOpenApi`.
        // ═════════════════════════════════════════════════════════════════════
        app.UseHbaOpenApi(FirstNonEmpty(
            serviceName,
            app.Configuration["SERVICE_NAME"],
            app.Configuration["ServiceName"],
            "service"));

        app.UseAuthentication();

        // Après l'authentification : sans cela, la partition par claim `sub` ne
        // verrait jamais l'utilisateur et retomberait sur l'adresse IP — donc sur
        // le CGNAT, ce que la partition par compte sert précisément à éviter.
        app.UseRateLimiter();

        app.UseAuthorization();

        // ═════════════════════════════════════════════════════════════════════
        // CONTEXTE PROPAGÉ DU §18 — APRÈS L'AUTHENTIFICATION, ET C'EST L'ESSENTIEL.
        //
        // Il capture l'acteur depuis `HttpContext.User`, vide tant que
        // `UseAuthentication` n'est pas passé. Placé plus haut, tout se remplirait
        // SAUF l'acteur — et une absence d'acteur ne lève aucune erreur : elle se
        // découvre des semaines plus tard, dans un journal d'audit vide.
        //
        // Enregistré ICI plutôt que dans chaque `Program.cs` : les quatorze hôtes
        // appellent déjà `UseHbaService`, donc `meta.requestId` et les codes
        // `<SERVICE>_SERVICE_NOT_FOUND` deviennent corrects partout sans qu'aucun
        // service ne soit modifié. Un branchement à faire quatorze fois est un
        // branchement qu'on oublie une fois.
        // ═════════════════════════════════════════════════════════════════════
        var resolvedName = FirstNonEmpty(
            serviceName,
            app.Configuration["SERVICE_NAME"],
            app.Configuration["ServiceName"],
            "unknown-service");

        app.UseHbaRequestContext(resolvedName, serviceCode);

        // `AllowAnonymous` explicite : sans lui, Docker reçoit 401, déclare le
        // conteneur malsain et le redémarre en boucle sans erreur applicative.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            // POUR UN SERVICE, LA BASE EST CRITIQUE — CONTRAIREMENT À LA PASSERELLE.
            //
            // La passerelle reste apte quand un service amont tombe : elle rend 502
            // sur une route et sert les quatorze autres. Un service sans sa base ne
            // peut RIEN servir : le sortir de la rotation est le comportement juste.
            Predicate = check => check.Tags.Contains("ready")
        }).AllowAnonymous();

        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        return app;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate!;
            }
        }

        return "unknown-service";
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE SOCLE DE SÉCURITÉ SEUL, POUR LES HÔTES SANS BASE DE DONNÉES.
    ///
    /// ÉCRIT PARCE QUE DIX SERVICES N'AVAIENT AUCUNE AUTHENTIFICATION.
    ///
    /// `AddHbaService&lt;TDbContext&gt;` exige un DbContext et un module métier. Les
    /// satellites — tarification de livraison, répartition, livreurs, itinéraires,
    /// suivi, preuve, menus, disponibilité, cuisine, avis food — n'ont ni l'un ni
    /// l'autre : plusieurs tiennent leur état dans un dictionnaire en mémoire. Ils
    /// ne pouvaient donc pas appeler le socle complet, et personne n'a écrit le
    /// socle partiel. Résultat : leurs `Program.cs` n'appelaient ni
    /// `UseAuthentication` ni `UseAuthorization`, et TOUTE leur surface était
    /// publique — y compris `POST /api/v1/admin/delivery-pricing/rules`, qui fixe
    /// le prix des courses de la plateforme entière.
    ///
    /// Le fait qu'aucun d'eux ne soit routé par la passerelle aujourd'hui n'est pas
    /// un contrôle : c'est une coïncidence de déploiement, qui cesse le jour où
    /// l'un d'eux est exposé.
    ///
    /// CE QUE CETTE PAIRE FAIT, ET CE QU'ELLE NE FAIT PAS.
    ///
    /// Elle pose l'authentification JWT et la politique de repli — « au moins un
    /// compte » — et rien d'autre. Pas de MediatR, pas de cache, pas de sonde de
    /// base, pas de pipeline de blocs applicatifs : un hôte qui n'a pas de domaine
    /// n'a pas à payer pour un socle qu'il n'utilise pas.
    ///
    /// Un service qui GAGNE une base de données doit passer à
    /// `AddHbaService&lt;TDbContext&gt;` : ce n'est pas un raccourci permanent, c'est
    /// le minimum pour qu'un hôte ne serve pas ses écritures à un inconnu.
    ///
    /// LES SONDES DE SANTÉ DOIVENT PORTER `AllowAnonymous` EXPLICITEMENT.
    /// La politique de repli les fermerait sinon, et Docker redémarrerait le
    /// conteneur en boucle sur un 401 — sans aucune erreur applicative pour
    /// l'expliquer. C'est la même précaution que dans `UseHbaService`.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static WebApplicationBuilder AddHbaSecurity(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        ConfigureForwardedHeaders(services);

        services.AddProblemDetails();
        services.AddHttpContextAccessor();

        services.Configure<InternalCallOptions>(
            configuration.GetSection(InternalCallOptions.SectionName));

        AddAuthentication(services, configuration);

        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return builder;
    }

    /// <summary>
    /// Le pendant de <see cref="AddHbaSecurity"/> dans le pipeline. À appeler AVANT
    /// tout `Map*` : un middleware n'agit que sur ce qui le suit.
    /// </summary>
    public static WebApplication UseHbaSecurity(this WebApplication app)
    {
        // Même réglage que le socle complet : `AddHbaSecurity` a configuré les
        // en-têtes de mandataire, il faut encore les appliquer. Configurer une
        // option sans jamais poser le middleware est le genre de demi-mesure qui
        // se lit comme un correctif et n'en est pas un.
        if (!string.Equals(app.Configuration["ProxyTrust:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            app.UseForwardedHeaders();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// L'ADRESSE DU CLIENT, ET NON CELLE DE LA PASSERELLE.
    ///
    /// SANS CECI, LE LIMITEUR DE DÉBIT PROTÉGEAIT LA PLATEFORME ENTIÈRE AVEC
    /// UN SEUL QUOTA.
    ///
    /// `AuthRateLimiter` partitionne par claim `sub` quand il y en a un, et
    /// retombe sur l'adresse IP sinon. Or les routes qu'il protège sont
    /// précisément celles où il n'y a PAS de `sub` : connexion, mot de passe
    /// oublié, vérification d'OTP. Derrière la passerelle, `RemoteIpAddress` est
    /// l'adresse de la PASSERELLE — identique pour tout le monde.
    ///
    /// Conséquence : trente tentatives par minute pour l'ensemble des
    /// utilisateurs, et un attaquant qui les consomme seul verrouille la connexion
    /// de tous les autres. Le limiteur devenait l'arme au lieu du bouclier.
    ///
    /// POURQUOI ON NE LIT PAS `X-Forwarded-For` À LA MAIN.
    ///
    /// Lire l'en-tête directement dans `ClientIp` serait plus court et STRICTEMENT
    /// PIRE : n'importe quel client pourrait alors se déclarer une adresse
    /// différente à chaque requête et contourner le limiteur entièrement. L'en-tête
    /// n'a de valeur que s'il vient d'un mandataire de confiance — c'est tout
    /// l'objet de ce réglage.
    ///
    /// LA CONFIANCE EST BORNÉE AUX RÉSEAUX PRIVÉS, ET C'EST DÉLIBÉRÉ.
    ///
    /// Les services ne publient aucun port : dans le compose comme dans le
    /// cluster, seule la passerelle les atteint. Faire confiance aux plages
    /// privées revient donc à faire confiance à la passerelle. Si un jour un
    /// service devient joignable de l'extérieur, ce réglage doit être resserré sur
    /// l'adresse exacte du mandataire — `KnownProxies` est là pour cela.
    ///
    /// Réglable par `ProxyTrust:Enabled=false` : l'en-tête est alors ignoré et l'on
    /// retombe sur le comportement d'avant, qui est sûr mais inefficace.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static void ConfigureForwardedHeaders(IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Par défaut la bibliothèque ne fait confiance qu'au bouclage, ce qui
            // exclut la passerelle : on vide, puis on déclare les plages privées.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            // `new(...)` CIBLÉ, ET NON `new IPNetwork(...)`.
            //
            // Deux types portent ce nom : `System.Net.IPNetwork` (depuis .NET 8) et
            // `Microsoft.AspNetCore.HttpOverrides.IPNetwork`. Les deux espaces de
            // noms sont importés ici, donc écrire le nom simple donnerait CS0104 —
            // et choisir l'un des deux à la main casserait le jour où la propriété
            // changera de type entre deux versions du framework. Le `new` ciblé
            // laisse le compilateur lire le type dans la collection.
            options.KnownNetworks.Add(new(IPAddress.Parse("10.0.0.0"), 8));
            options.KnownNetworks.Add(new(IPAddress.Parse("172.16.0.0"), 12));
            options.KnownNetworks.Add(new(IPAddress.Parse("192.168.0.0"), 16));
            options.KnownNetworks.Add(new(IPAddress.Parse("127.0.0.0"), 8));

            // Une seule couche de mandataire entre le client et le service. Au-delà,
            // on prendrait l'adresse déclarée par un intermédiaire inconnu.
            options.ForwardLimit = 1;
        });
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"];
        var audience = configuration["Authentication:Audience"];
        var signingKey = configuration["Authentication:SigningKey"];

        // ═════════════════════════════════════════════════════════════════════
        // UNE CLÉ DE SIGNATURE ABSENTE N'EST PAS UNE CONFIGURATION PARTIELLE.
        //
        // Plus bas, `IssuerSigningKey` n'est posée que si la clé est renseignée,
        // alors que `ValidateIssuerSigningKey` reste à `true`. Sans clé, TOUT
        // jeton est donc rejeté — et le service démarre normalement, répond sur
        // ses routes anonymes, et rend 401 sur toutes les autres.
        //
        // C'est le mode de panne le plus coûteux de ce fichier : rien n'échoue
        // au démarrage, les sondes passent, et le symptôme — « 401 partout » —
        // ressemble à un problème de jeton côté appelant. `docker-compose.dev.yml`
        // documente déjà exactement cette journée perdue.
        //
        // Hors Development, on refuse de démarrer. Le coût est un service qui ne
        // part pas ; le gain est un message qui nomme la cause à la seconde où
        // elle existe.
        // ═════════════════════════════════════════════════════════════════════
        if (string.IsNullOrWhiteSpace(signingKey)
            && EnvironnementDeploiement.EstProduction(configuration))
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey est absente. Le service démarrerait en "
                + "rejetant TOUS les jetons — `ValidateIssuerSigningKey` reste actif "
                + "sans clé à comparer — et chaque appel authentifié rendrait 401 "
                + "sans qu'aucune erreur de démarrage ne l'explique. "
                + "Renseigner AUTHENTICATION__SIGNINGKEY.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // Cinq minutes par défaut chez .NET : un jeton révoqué resterait
                    // accepté pendant tout ce temps.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    NameClaimType = "sub",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                if (!string.IsNullOrWhiteSpace(signingKey))
                {
                    bearer.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));

                    // Épinglage : ferme la confusion d'algorithme (`alg: none`,
                    // bascule vers un algorithme asymétrique à clé choisie).
                    bearer.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.HmacSha256];
                }
            });

        // LE SERVICE REVALIDE LE JETON, MÊME DERRIÈRE LA PASSERELLE.
        //
        // La passerelle n'est pas l'unique frontière de sécurité : sur le réseau
        // `hba-backend`, tout service peut en appeler un autre directement. Se
        // reposer sur la passerelle rendrait chaque service ouvert à quiconque
        // atteint ce réseau — un conteneur compromis, un job de maintenance, une
        // erreur de configuration Docker.
    }
}
