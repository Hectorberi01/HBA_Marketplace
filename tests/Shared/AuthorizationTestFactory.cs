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
public class AuthorizationTestFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint>
    where TEntryPoint : class
{
    /// <summary>
    /// Toute la configuration nécessaire au DÉMARRAGE, posée en variables
    /// d'environnement.
    /// </summary>
    static AuthorizationTestFactory()
    {
        static void Poser(string cle, string valeur)
            => Environment.SetEnvironmentVariable(cle, valeur);

        Poser("OUTBOX_ENABLED", "false");
        Poser("ASPNETCORE_ENVIRONMENT", "Testing");

        // Clé de test explicite : ne pas dépendre de celle des appsettings, qu'un
        // développeur peut changer sans se douter qu'il casse la suite.
        Poser("Authentication__SigningKey", TestTokens.SigningKey);
        Poser("Authentication__Issuer", TestTokens.Issuer);
        Poser("Authentication__Audience", TestTokens.Audience);

        // Clé interne : `AddHbaGrpc` la réclame pour l'interception de service à
        // service.
        Poser("Internal__ApiKey", "cle-interne-de-test");

        // UNE VRAIE PAIRE DE CLÉS, ET NON LE MODE NON SIGNÉ.
        var nomDeLHote = typeof(TEntryPoint).Assembly.GetName().Name!;

        using (var identite = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            Poser("Internal__ServiceName", nomDeLHote);
            Poser("Internal__PrivateKey",
                Convert.ToBase64String(identite.ExportPkcs8PrivateKey()));
            Poser("Internal__PublicKeys",
                $"{nomDeLHote}={Convert.ToBase64String(identite.ExportSubjectPublicKeyInfo())}");
        }

        // Chaîne SYNTAXIQUEMENT valide vers un port fermé, avec des délais courts :
        // le conteneur se construit, et le premier handler qui touche la base
        // échoue tout de suite au lieu d'attendre les quinze secondes par défaut de
        // Npgsql.
        Poser(
            "ConnectionStrings__Default",
            "Host=127.0.0.1;Port=59432;Database=hba_tests;Username=hba;Password=hba;"
            + "Timeout=1;Command Timeout=1");

        Poser("Database__MigrateOnStartup", "false");
        Poser("Kafka__Enabled", "false");
        Poser("Kafka__BootstrapServers", "127.0.0.1:59092");
        // VIDE, ET NON UNE ADRESSE VERS UN PORT FERMÉ.
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

        // LES NEUF QUI MANQUAIENT, ET CE QUE LEUR ABSENCE COÛTAIT.
        Poser("Services__FoodCart", "http://127.0.0.1:59114");
        Poser("Services__FoodOrder", "http://127.0.0.1:59115");
        Poser("Services__Promotion", "http://127.0.0.1:59116");
        Poser("Services__Drivers", "http://127.0.0.1:59117");
        Poser("Services__DeliveryPricing", "http://127.0.0.1:59122");

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
        Poser("Services__Routes", "http://127.0.0.1:59120");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // CONSERVÉ, MAIS CE N'EST PAS CE QUI FAIT TENIR L'HÔTE.
        builder.UseEnvironment("Testing");

        // LES TROIS VALEURS D'AUTHENTIFICATION SONT POSÉES ICI AUSSI, ET C'EST UNE
        // CEINTURE DÉLIBÉRÉE.
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
    /// Refus acceptables sur la ressource d'autrui : 403 quand le service sait que
    /// la ressource existe et n'est pas à l'appelant, 404 quand il choisit de ne
    /// pas confirmer son existence.
    /// </summary>
    public static readonly HttpStatusCode[] RefusOuIntrouvable =
        [HttpStatusCode.Forbidden, HttpStatusCode.NotFound];
}
