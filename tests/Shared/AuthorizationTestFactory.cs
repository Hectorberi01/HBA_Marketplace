using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Tests.Authorization;

/// <summary>
/// Démarre un service HBA en mémoire, sans base, sans Kafka et sans voisin
/// joignable, pour n'éprouver QUE ses décisions d'autorisation.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CES TESTS TIENNENT SANS INFRASTRUCTURE.
///
/// Un 401 et un 403 se décident dans le pipeline, AVANT le handler : la
/// FallbackPolicy et les politiques de groupe s'appliquent sur les métadonnées
/// du point de terminaison, sans qu'une seule ligne de code métier ne s'exécute.
/// Aucune requête SQL, aucun appel gRPC. C'est ce qui rend ces vérifications
/// possibles ici, alors que PostgreSQL et les douze autres services sont absents.
///
/// COROLLAIRE : UN CODE QUI N'EST PAS 401/403 NE PROUVE PAS QUE ÇA MARCHE.
///
/// Une requête qui franchit l'autorisation atteint le handler, qui cherche sa
/// base et échoue en 500. Un 500 est donc, ici, la PREUVE que les contrôles ont
/// été franchis — l'inverse d'un échec. Les assertions sont écrites en
/// conséquence : `NotBe(Forbidden)` et non `Be(OK)`.
///
/// TROIS RÉGLAGES SANS LESQUELS L'HÔTE NE DÉMARRE PAS.
///
///   • `Database:MigrateOnStartup=false` — le défaut vaut VRAI en Development,
///     et `MigrateHbaDatabaseAsync` s'exécute AVANT `app.Run()` : l'hôte
///     attendrait PostgreSQL puis lèverait, avant la première requête.
///   • `Kafka:Enabled=false` — sinon le consommateur d'événements et le
///     producteur tentent de joindre un courtier absent.
///   • `Services:*` — `AddXxxGrpcClient` lève à la CONSTRUCTION de l'hôte quand
///     l'adresse manque. Les adresses doivent être valides ; elles n'ont pas à
///     répondre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public class AuthorizationTestFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>
    /// Toute la configuration nécessaire au DÉMARRAGE, posée en variables
    /// d'environnement.
    /// </summary>
    /// <remarks>
    /// ═════════════════════════════════════════════════════════════════════════
    /// `ConfigureAppConfiguration` ARRIVE TROP TARD. C'EST LE PIÈGE.
    ///
    /// Cette fabrique fournissait exactement ces valeurs, par
    /// `builder.ConfigureAppConfiguration(... AddInMemoryCollection ...)`. Les
    /// cinquante-neuf tests échouaient malgré tout, tous sur la même exception :
    ///
    ///     System.InvalidOperationException : Chaîne de connexion « Default » absente.
    ///     at …ModuleInstaller.Install(…)
    ///     at ServiceHostExtensions.AddHbaService(…)
    ///     at Program.&lt;Main&gt;$(String[] args)
    ///
    /// La pile dit tout : la levée se produit dans `Program.Main`, pendant
    /// `AddHbaService`. Or les treize services utilisent l'hébergement minimal —
    /// `WebApplication.CreateBuilder(args)` — et lisent leur configuration
    /// IMMÉDIATEMENT, ligne suivante. Les rappels d'`IWebHostBuilder.ConfigureAppConfiguration`,
    /// eux, ne sont exécutés qu'au `Build()` final, bien après. La configuration
    /// était donc parfaitement fournie… et parfaitement inutile.
    ///
    /// Les variables d'environnement, elles, sont lues par `CreateBuilder`
    /// lui-même, avant que la première ligne de `Program.Main` ne s'exécute.
    /// C'est le seul canal qui arrive à temps — et c'est déjà pour cette raison
    /// que `OUTBOX_ENABLED` était posé ici.
    ///
    /// Séparateur `__` et non `:` : c'est la convention des variables
    /// d'environnement. Une clé écrite `ConnectionStrings:Default` ici serait
    /// ignorée en silence, et l'on retrouverait l'exception ci-dessus.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// CE QUI DOIT ÊTRE POSÉ, ET POURQUOI
    ///
    ///   • `OUTBOX_ENABLED` — l'outbox est pilotée par variable d'environnement
    ///     et non par configuration (voir OutboxRegistration.Enabled) : sans
    ///     cela, un service d'arrière-plan interroge une base absente pendant
    ///     toute la suite.
    ///   • `Database__MigrateOnStartup` — le défaut vaut VRAI en Development, et
    ///     la migration s'exécute AVANT `app.Run()` : l'hôte attendrait
    ///     PostgreSQL puis lèverait.
    ///   • `Kafka__Enabled` — sinon consommateur et producteur cherchent un
    ///     courtier absent.
    ///   • `Services__*` — `AddXxxGrpcClient` lève à la CONSTRUCTION de l'hôte
    ///     quand l'adresse manque. Elles doivent être valides, pas joignables.
    ///   • `ASPNETCORE_ENVIRONMENT=Testing` — ni Development (qui migre), ni
    ///     Production (que PaymentsModuleInstaller sanctionne par un refus de
    ///     démarrer sans prestataire configuré).
    /// ═════════════════════════════════════════════════════════════════════════
    /// </remarks>
    static AuthorizationTestFactory()
    {
        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("OUTBOX_ENABLED", "false");
        Poser("ASPNETCORE_ENVIRONMENT", "Testing");

        // Clé de test explicite : ne pas dépendre de celle des appsettings,
        // qu'un développeur peut changer sans se douter qu'il casse la suite.
        Poser("Authentication__SigningKey", TestTokens.SigningKey);
        Poser("Authentication__Issuer", TestTokens.Issuer);
        Poser("Authentication__Audience", TestTokens.Audience);

        // Clé interne : `AddHbaGrpc` la réclame pour l'interception de service à
        // service. Sa valeur n'a aucune importance, sa présence si.
        Poser("Internal__ApiKey", "cle-interne-de-test");

        // ═════════════════════════════════════════════════════════════════
        // UNE VRAIE PAIRE DE CLÉS, ET NON LE MODE NON SIGNÉ.
        //
        // Premier essai : `Internal__IdentitesNonSignees=true`, en supposant un
        // hôte de test en `Development`. IL NE L'EST PAS, et la raison est écrite
        // quelques lignes plus haut, dans ce fichier : `ASPNETCORE_ENVIRONMENT`
        // vaut délibérément `Testing` — ni `Development`, qui migrerait la base,
        // ni `Production`, que `PaymentsModuleInstaller` sanctionne.
        //
        // Les 253 tests tombaient donc tous sur le garde de `AddHbaGrpc`, qui a
        // fait exactement son travail. Le raccourci supposait un environnement
        // que le réglage voisin interdit ; le supposer plutôt que le lire est
        // précisément l'erreur.
        //
        // Engendrer la paire coûte environ une milliseconde par hôte de test, et
        // rapporte davantage que le raccourci qu'elle remplace : le chemin
        // RÉELLEMENT exécuté en production — frappe, signature, vérification,
        // table d'autorisations — est celui que les tests empruntent.
        //
        // LE NOM EST CELUI DE L'ASSEMBLY DU POINT D'ENTRÉE, PAS DE L'ASSEMBLY
        //    DE TEST.
        //
        // À défaut de `Internal:ServiceName`, l'identité se replie sur
        // `Assembly.GetEntryAssembly()` — sous xUnit, l'assembly de TEST, absente
        // de `AutorisationsGrpc`. Tout appel gRPC partirait en `PermissionDenied`,
        // et l'erreur désignerait la table plutôt que le repli qui l'a produite.
        //
        // Le registre ne contient que cet hôte : il n'y a pas d'autre appelant à
        // vérifier en test, et une entrée de plus n'attesterait rien.
        // ═════════════════════════════════════════════════════════════════
        var nomDeLHote = typeof(TEntryPoint).Assembly.GetName().Name!;

        using (var identite = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Poser("Internal__ServiceName", nomDeLHote);
            Poser("Internal__PrivateKey",
                Convert.ToBase64String(identite.ExportPkcs8PrivateKey()));
            Poser("Internal__PublicKeys",
                $"{nomDeLHote}={Convert.ToBase64String(identite.ExportSubjectPublicKeyInfo())}");
        }

        // Chaîne SYNTAXIQUEMENT valide vers un port fermé, avec des délais
        // courts : le conteneur se construit, et le premier handler qui touche
        // la base échoue tout de suite au lieu d'attendre les quinze secondes
        // par défaut de Npgsql.
        Poser(
            "ConnectionStrings__Default",
            "Host=127.0.0.1;Port=59432;Database=hba_tests;Username=hba;Password=hba;"
            + "Timeout=1;Command Timeout=1");

        Poser("Database__MigrateOnStartup", "false");
        Poser("Kafka__Enabled", "false");
        Poser("Kafka__BootstrapServers", "127.0.0.1:59092");
        // ═════════════════════════════════════════════════════════════════════
        // VIDE, ET NON UNE ADRESSE VERS UN PORT FERMÉ.
        //
        // Cette clé valait « 127.0.0.1:59379 » — le même procédé que pour
        // PostgreSQL : une adresse syntaxiquement valide vers rien. Elle était
        // sans conséquence tant que le socle ignorait Redis. Depuis qu'il le
        // branche, la même valeur ferait tenter une CONNEXION à chaque lecture de
        // cache, avec le délai d'attente par défaut de StackExchange.Redis —
        // plusieurs secondes, multipliées par le nombre de routes éprouvées.
        //
        // Vide, le socle retombe sur le cache mémoire : c'est exactement ce que
        // ces tests veulent, puisqu'ils n'éprouvent que des décisions
        // d'autorisation et n'ont aucune réplique à synchroniser.
        // ═════════════════════════════════════════════════════════════════════
        Poser("Redis__ConnectionString", string.Empty);

        Poser("Services__Identity", "http://127.0.0.1:59101");
        Poser("Services__User", "http://127.0.0.1:59102");
        Poser("Services__Catalog", "http://127.0.0.1:59103");
        Poser("Services__Inventory", "http://127.0.0.1:59104");
        Poser("Services__Commerce", "http://127.0.0.1:59105");
        Poser("Services__Order", "http://127.0.0.1:59106");
        Poser("Services__Ordering", "http://127.0.0.1:59106");
        Poser("Services__Merchant", "http://127.0.0.1:59107");
        Poser("Services__Delivery", "http://127.0.0.1:59108");
        Poser("Services__Food", "http://127.0.0.1:59109");
        Poser("Services__Media", "http://127.0.0.1:59110");
        Poser("Services__Financial", "http://127.0.0.1:59111");
        Poser("Services__Engagement", "http://127.0.0.1:59112");
        Poser("Services__Communication", "http://127.0.0.1:59113");

        // ═════════════════════════════════════════════════════════════════════
        // LES NEUF QUI MANQUAIENT, ET CE QUE LEUR ABSENCE COÛTAIT.
        //
        // Cette liste était tenue À LA MAIN, au rythme des besoins : on y
        // ajoutait une clé le jour où un test échouait. Le jour où
        // payment-service a gagné un client vers food-order-service (lot 6.1),
        // les CINQUANTE-NEUF tests d'autorisation financiers sont tombés d'un
        // coup — tous sur « Services:FoodOrder est absent », levé à la
        // construction de l'hôte, avant la moindre décision d'autorisation.
        //
        // Le défaut n'est pas l'oubli : c'est que rien ne reliait cette liste à
        // celle des clients qui la réclament. `check-service-addresses.py`
        // vérifiait le compose ET le configmap Kubernetes, PAS ce fichier — le
        // troisième endroit où la même clé doit exister. Il le vérifie désormais.
        //
        // Les neuf ci-dessous ne servent aucun test d'aujourd'hui. Elles sont
        // posées quand même : la prochaine référence de projet vers l'un de ces
        // services ferait retomber une suite entière, et le message d'erreur ne
        // désignerait toujours pas ce fichier.
        // ═════════════════════════════════════════════════════════════════════
        Poser("Services__FoodCart", "http://127.0.0.1:59114");
        Poser("Services__FoodOrder", "http://127.0.0.1:59115");
        Poser("Services__Promotion", "http://127.0.0.1:59116");
        Poser("Services__Drivers", "http://127.0.0.1:59117");
        Poser("Services__DeliveryPricing", "http://127.0.0.1:59122");

        // ═════════════════════════════════════════════════════════════════════
        // `Services__Routes` RESTE, LES TROIS AUTRES SONT PARTIES — ET LA
        // DIFFÉRENCE TIENT À UNE SEULE CHOSE.
        //
        // `Services__Dispatch`, `Services__Tracking` et `Services__ProofOfDelivery`
        // désignaient des services retirés du dépôt (D42, D43). Leurs
        // enregistrements de client gRPC sont partis avec eux : plus personne ne
        // peut lire ces clés, ni aujourd'hui ni demain.
        //
        // `Services__Routes` EST DIFFÉRENTE, et j'ai d'abord cru le contraire.
        // `route-service` n'a aucun appelant et aucune entrée dans
        // `ServicesOptions` de la passerelle — c'est exact. Mais
        // `RoutesGrpcRegistration.AddRoutesGrpcClient` existe toujours, et LÈVE si
        // `Services:Routes` est absent. Aucun hôte ne l'appelle aujourd'hui ; le
        // jour où l'un le fera, ses tests d'autorisation échoueraient À LA
        // CONSTRUCTION, sur un message qui ne dit rien de cette ligne.
        //
        // C'est `check-service-addresses.py` qui l'a signalé, après que je l'aie
        // retirée. Le contrôle avait raison et pas moi : on ne retire pas une
        // adresse parce qu'on croit qu'elle ne sert pas, on la retire quand le
        // code qui la lit a disparu.
        // ═════════════════════════════════════════════════════════════════════
        Poser("Services__Routes", "http://127.0.0.1:59120");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // CONSERVÉ, MAIS CE N'EST PAS CE QUI FAIT TENIR L'HÔTE.
        //
        // `UseEnvironment` passe par la configuration d'hôte, qui elle arrive à
        // temps. La variable d'environnement posée plus haut suffirait ; on
        // garde l'appel parce qu'il rend l'intention lisible à qui ouvre ce
        // fichier par le milieu.
        builder.UseEnvironment("Testing");

        // ═════════════════════════════════════════════════════════════════════
        // LES TROIS VALEURS D'AUTHENTIFICATION SONT POSÉES ICI AUSSI, ET C'EST
        //    UNE CEINTURE DÉLIBÉRÉE.
        //
        // Elles le sont déjà par variable d'environnement, dans le constructeur
        // statique. Ça devrait suffire — mais un constructeur statique s'exécute
        // au premier accès au type, et l'ordre exact par rapport à la
        // construction de l'hôte dépend de détails qu'on ne contrôle pas depuis
        // ce fichier.
        //
        // CE QUE COÛTE L'ÉCHEC DE CE PARI. `AddAuthentication` ne pose
        // `IssuerSigningKey` que si la clé est renseignée, alors que
        // `ValidateIssuerSigningKey` reste actif : sans clé, TOUT jeton est
        // rejeté. La suite entière rend alors 401 là où elle attend 403 — et le
        // message ne parle jamais de configuration.
        //
        // `UseSetting` passe par la configuration d'HÔTE, lue par
        // `CreateBuilder` lui-même. C'est le seul canal, avec les variables
        // d'environnement, qui arrive à coup sûr avant la construction.
        // ═════════════════════════════════════════════════════════════════════
        builder.UseSetting("Authentication:SigningKey", TestTokens.SigningKey);
        builder.UseSetting("Authentication:Issuer", TestTokens.Issuer);
        builder.UseSetting("Authentication:Audience", TestTokens.Audience);

        builder.ConfigureTestServices(ConfigureTestDoubles);
    }

    /// <summary>
    /// Remplace les clients vers les services voisins. Ils ne sont substitués que
    /// là où l'AUTORISATION en dépend : `EnsureSellerAsync` et `EnsureDriverAsync`
    /// interrogent un voisin AVANT de toucher la base, et c'est précisément ce
    /// point de décision que ces tests éprouvent.
    /// </summary>
    protected virtual void ConfigureTestDoubles(IServiceCollection services)
    {
    }

    /// <summary>Un client porteur d'un jeton forgé.</summary>
    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>Envoi de requêtes et codes attendus, communs aux trois suites.</summary>
public static class Requetes
{
    /// <summary>
    /// Envoie une requête sur la méthode voulue, avec un corps JSON vide quand la
    /// méthode en attend un.
    /// </summary>
    /// <remarks>
    /// Le corps est délibérément `{}` et non un objet valide : l'autorisation est
    /// évaluée AVANT la liaison de modèle. Un test qui aurait besoin d'un corps
    /// correct pour obtenir un 403 testerait autre chose que l'autorisation.
    /// </remarks>
    public static Task<HttpResponseMessage> EnvoyerAsync(HttpClient client, string methode, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(methode), route);

        if (methode is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return client.SendAsync(request);
    }

    /// <summary>
    /// Refus acceptables sur la ressource d'autrui : 403 quand le service sait
    /// que la ressource existe et n'est pas à l'appelant, 404 quand il choisit de
    /// ne pas confirmer son existence. Les deux ferment la porte.
    /// </summary>
    public static readonly HttpStatusCode[] RefusOuIntrouvable =
        [HttpStatusCode.Forbidden, HttpStatusCode.NotFound];
}
