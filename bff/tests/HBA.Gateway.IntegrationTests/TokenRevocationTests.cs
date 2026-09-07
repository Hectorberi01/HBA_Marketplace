using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using HBA.Identity.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

/// <summary>
/// Le contrôle de révocation à la passerelle (ISSUE-022, décision D27).
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CES TESTS EMPÊCHENT DE REVENIR.
///
/// `IdentityModuleApi.ValidateAccessTokenAsync` compare le `security_stamp` du
/// jeton à celui du compte — le seul contrôle capable de refuser un jeton
/// cryptographiquement valide mais métier-mort. Elle était écrite, complète, et
/// n'avait AUCUN appelant. Déconnexion, changement de mot de passe et suspension
/// n'invalidaient donc rien pendant quinze minutes.
///
/// UNE FABRIQUE PAR TEST, ET CE N'EST PAS DU GASPILLAGE.
///
/// Le middleware mémorise son verdict par empreinte de jeton dans l'`IMemoryCache`
/// de l'hôte. Partager la fabrique entre les tests — le motif `IClassFixture` du
/// reste de ce projet — ferait fuir le cache et le compteur d'appels de l'un dans
/// l'autre, et l'ordre d'exécution de xunit déciderait du résultat. Les tests sur
/// la mémorisation, eux, n'ont de sens que sur un cache neuf.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class TokenRevocationTests
{
    /// <summary>Route protégée quelconque : seule compte la traversée du pipeline.</summary>
    private const string RouteProtegee = "/api/orders/mine";

    /// <summary>
    /// LE TEST CENTRAL : UN JETON MORT NE PASSE PLUS.
    ///
    /// Sans le middleware, cette requête franchissait tout — le jeton est
    /// parfaitement signé, non expiré, et l'autorisation n'a rien à y redire. Seul
    /// identity sait qu'il ne vaut plus rien.
    /// </summary>
    [Fact]
    public async Task Un_jeton_revoque_est_refuse()
    {
        var identite = IdentiteFictive.Repond(valide: false);
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        reponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// LE REFUS DOIT ÊTRE LISIBLE PAR UNE APPLICATION, PAS SEULEMENT PAR UN HUMAIN.
    ///
    /// Sans `WWW-Authenticate: Bearer error="invalid_token"`, une application
    /// mobile ne distingue pas ce 401 d'un droit manquant : elle réessaie la même
    /// requête avec le même jeton, indéfiniment, au lieu d'aller en redemander un.
    /// </summary>
    [Fact]
    public async Task Le_refus_dit_a_l_application_que_le_jeton_est_mort()
    {
        var identite = IdentiteFictive.Repond(valide: false);
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        // LU EN BRUT, PAS VIA `Headers.WwwAuthenticate`.
        //
        // La collection typée est PARSÉE : si `AuthenticationHeaderValue` n'arrive
        // pas à lire la valeur, l'en-tête bascule silencieusement dans les non
        // parsés et la collection se présente vide. Le test échouerait alors en
        // désignant l'absence d'un en-tête pourtant bien envoyé.
        reponse.Headers.TryGetValues("WWW-Authenticate", out var defi).Should().BeTrue();
        string.Join(' ', defi!).Should().Contain("invalid_token");

        reponse.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// LE REFUS NE DIT PAS POURQUOI, ET C'EST DÉLIBÉRÉ.
    ///
    /// Distinguer « compte suspendu » de « mot de passe changé » renseignerait
    /// quiconque détient un jeton volé sur ce que le propriétaire légitime vient de
    /// faire — donc sur le temps qu'il lui reste avant d'être découvert.
    /// </summary>
    [Fact]
    public async Task Le_refus_ne_divulgue_pas_la_cause()
    {
        var identite = IdentiteFictive.Repond(
            valide: false, motif: "compte suspendu par la modération");

        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());
        var corps = await reponse.Content.ReadAsStringAsync();

        corps.Should().NotContain("suspendu");
        corps.Should().NotContain("modération");
    }

    /// <summary>
    /// ET SURTOUT : UN JETON VIVANT PASSE.
    ///
    /// Un contrôle de sécurité qui refuse tout le monde « fonctionne » aussi. Le
    /// 502 attendu ici — order-service n'existe pas dans ce test — est la preuve
    /// que la requête est allée jusqu'au routage.
    /// </summary>
    [Fact]
    public async Task Un_jeton_vivant_franchit_le_controle()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        reponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// L'ÉCHEC EST OUVERT — C'EST LA DÉCISION D27, ET ELLE SE TESTE.
    ///
    /// Fermer signifierait qu'une panne d'identity rende 401 à toute la plateforme,
    /// paiements en cours compris : l'indisponibilité d'un service deviendrait
    /// celle de la plateforme entière. Ouvert, un compte suspendu garde ses droits
    /// PENDANT la panne — exactement le risque subi en permanence avant ISSUE-022,
    /// mais réduit aux minutes d'un incident.
    ///
    /// Si quelqu'un « durcit » ce comportement sans rouvrir la décision, ce test
    /// tombe et l'oblige à le faire sciemment.
    /// </summary>
    [Fact]
    public async Task Identity_injoignable_laisse_passer_plutot_que_de_fermer_la_plateforme()
    {
        var identite = IdentiteFictive.Leve();
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        reponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// UNE REQUÊTE ANONYME NE COÛTE PAS UN APPEL À IDENTITY.
    ///
    /// C'est ce qui borne la charge, et c'est aussi ce qui rend le cache
    /// ingonflable de l'extérieur : le middleware s'exécute APRÈS
    /// `UseAuthentication`, donc un jeton mal signé — ou absent — n'atteint jamais
    /// identity. Sans cette propriété, n'importe qui pourrait faire enfler la
    /// mémoire de la passerelle avec des jetons inventés.
    /// </summary>
    [Fact]
    public async Task Une_requete_sans_jeton_n_interroge_jamais_identity()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        await usine.CreateClient().GetAsync(RouteProtegee);

        identite.Appels.Should().Be(0);
    }

    /// <summary>
    /// LE VERDICT EST MÉMORISÉ, SINON IDENTITY DEVIENT UN POINT DE PANNE UNIQUE.
    ///
    /// Sans mémorisation, chaque requête de la plateforme entraînerait un appel
    /// gRPC : la latence d'identity deviendrait celle de tout le trafic. C'est
    /// précisément ce que D27 refusait en écartant le contrôle du socle partagé.
    /// </summary>
    [Fact]
    public async Task Une_rafale_sur_la_meme_session_ne_produit_qu_un_appel()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        var jeton = TestTokens.Create();

        await usine.AppelerAsync(RouteProtegee, jeton);
        await usine.AppelerAsync(RouteProtegee, jeton);
        await usine.AppelerAsync(RouteProtegee, jeton);

        identite.Appels.Should().Be(1);
    }

    /// <summary>
    /// ET LA MÉMORISATION EST PAR JETON, PAS GLOBALE.
    ///
    /// Une clé de cache trop large — par utilisateur, ou pire, unique — ferait
    /// hériter la seconde session du verdict rendu sur la première. Un compte
    /// déconnecté sur un appareil resterait valide sur l'autre, ou l'inverse.
    /// </summary>
    [Fact]
    public async Task Deux_sessions_distinctes_sont_verifiees_separement()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        await usine.AppelerAsync(RouteProtegee, TestTokens.Create());
        await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        identite.Appels.Should().Be(2);
    }
}

/// <summary>
/// La passerelle, avec un identity-service qui répond ce qu'on lui dit de répondre.
/// </summary>
/// <remarks>
/// `ConfigureTestServices` ET NON `ConfigureServices`.
///
/// Le premier s'exécute APRÈS le `Program` de l'application ; le second avant.
/// `AddIdentityGrpcClient` enregistre `IIdentityModuleApi` en Scoped depuis
/// `Program` : une substitution posée trop tôt serait écrasée par la vraie, sans
/// erreur, et les tests passeraient en interrogeant un service absent — donc en
/// éprouvant l'échec ouvert à chaque fois, y compris là où ils croient éprouver un
/// refus.
/// </remarks>
public sealed class RevocationFactory : GatewayFactory
{
    private readonly IIdentityModuleApi _identite;

    public RevocationFactory(IIdentityModuleApi identite) => _identite = identite;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityModuleApi>();
            services.AddSingleton(_identite);
        });
    }

    /// <summary>Une requête authentifiée par le jeton donné.</summary>
    public Task<HttpResponseMessage> AppelerAsync(string route, string jeton)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jeton);

        return client.GetAsync(route);
    }
}

/// <summary>
/// Un identity-service de test qui compte ce qu'on lui demande.
/// </summary>
/// <remarks>
/// LES AUTRES MEMBRES LÈVENT PLUTÔT QUE DE RENDRE UNE VALEUR NEUTRE.
///
/// Rendre `null` ou une liste vide ferait passer en silence un test qui
/// emprunterait un chemin imprévu — et l'on croirait avoir éprouvé la révocation
/// alors qu'on aurait éprouvé autre chose. L'exception dit lequel.
/// </remarks>
public sealed class IdentiteFictive : IIdentityModuleApi
{
    private readonly Func<AccessTokenValidation> _verdict;

    private int _appels;

    private IdentiteFictive(Func<AccessTokenValidation> verdict) => _verdict = verdict;

    /// <summary>Nombre d'appels réellement parvenus jusqu'ici.</summary>
    public int Appels => Volatile.Read(ref _appels);

    public static IdentiteFictive Repond(bool valide, string? motif = null)
        => new(() => new AccessTokenValidation(
            valide, Guid.NewGuid(), [], [], valide ? null : motif ?? "revoked"));

    /// <summary>identity injoignable : le client gRPC lèverait de la même façon.</summary>
    public static IdentiteFictive Leve()
        => new(() => throw new InvalidOperationException("identity-service injoignable"));

    public Task<AccessTokenValidation> ValidateAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _appels);

        return Task.FromResult(_verdict());
    }

    public Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par le contrôle de révocation.");

    public Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par le contrôle de révocation.");

    public Task<UserAuthorization?> GetUserRolesAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par le contrôle de révocation.");

    public Task<int> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non sollicité par le contrôle de révocation.");
}
