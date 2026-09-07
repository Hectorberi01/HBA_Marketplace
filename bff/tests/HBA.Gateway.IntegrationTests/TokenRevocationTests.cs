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

/// <summary>Le contrôle de révocation à la passerelle (ISSUE-022, décision D27).</summary>
public sealed class TokenRevocationTests
{
    /// <summary>Route protégée quelconque : seule compte la traversée du pipeline.</summary>
    private const string RouteProtegee = "/api/orders/mine";

    /// <summary>LE TEST CENTRAL : UN JETON MORT NE PASSE PLUS.</summary>
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
    /// </summary>
    [Fact]
    public async Task Le_refus_dit_a_l_application_que_le_jeton_est_mort()
    {
        var identite = IdentiteFictive.Repond(valide: false);
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        // LU EN BRUT, PAS VIA `Headers.WwwAuthenticate`.
        reponse.Headers.TryGetValues("WWW-Authenticate", out var defi).Should().BeTrue();
        string.Join(' ', defi!).Should().Contain("invalid_token");

        reponse.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>LE REFUS NE DIT PAS POURQUOI, ET C'EST DÉLIBÉRÉ.</summary>
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

    /// <summary>ET SURTOUT : UN JETON VIVANT PASSE.</summary>
    [Fact]
    public async Task Un_jeton_vivant_franchit_le_controle()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        reponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>L'ÉCHEC EST OUVERT — C'EST LA DÉCISION D27, ET ELLE SE TESTE.</summary>
    [Fact]
    public async Task Identity_injoignable_laisse_passer_plutot_que_de_fermer_la_plateforme()
    {
        var identite = IdentiteFictive.Leve();
        using var usine = new RevocationFactory(identite);

        var reponse = await usine.AppelerAsync(RouteProtegee, TestTokens.Create());

        reponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>UNE REQUÊTE ANONYME NE COÛTE PAS UN APPEL À IDENTITY.</summary>
    [Fact]
    public async Task Une_requete_sans_jeton_n_interroge_jamais_identity()
    {
        var identite = IdentiteFictive.Repond(valide: true);
        using var usine = new RevocationFactory(identite);

        await usine.CreateClient().GetAsync(RouteProtegee);

        identite.Appels.Should().Be(0);
    }

    /// <summary>LE VERDICT EST MÉMORISÉ, SINON IDENTITY DEVIENT UN POINT DE PANNE UNIQUE.</summary>
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

    /// <summary>ET LA MÉMORISATION EST PAR JETON, PAS GLOBALE.</summary>
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

/// <summary>Un identity-service de test qui compte ce qu'on lui demande.</summary>
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
