using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class AuthorizationTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public AuthorizationTests(GatewayFactory factory) => _factory = factory;

    /// <summary>Les routes d'authentification restent ouvertes, sinon nul ne peut se connecter.</summary>
    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/refresh")]
    public async Task Les_routes_publiques_ne_rendent_pas_401(string route)
    {
        var response = await _factory.CreateClient()
            .PostAsync(route, new StringContent(string.Empty));

        // 502 : identity-service n'existe pas dans ce test. C'est la PREUVE que
        // la requête a franchi authentification et autorisation — un 401 ici
        // signifierait que la route publique a été fermée par erreur.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// LE TEST QUI VÉRIFIE LE SENS DE L'OUBLI.
    ///
    /// La politique de repli ferme tout point de terminaison qui ne déclare rien.
    /// Si quelqu'un la retire, ces routes deviendraient publiques — et rien
    /// d'autre ne le signalerait, puisqu'elles continueraient de fonctionner.
    /// </summary>
    [Theory]
    [InlineData("/api/orders/mine")]
    [InlineData("/api/wallet/balance")]
    [InlineData("/api/cart")]
    [InlineData("/api/users/me")]
    [InlineData("/api/payments/methods")]
    public async Task Les_routes_protegees_rendent_401_sans_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_valide_franchit_l_autorisation()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create("Buyer"));

        var response = await client.GetAsync("/api/orders/mine");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// UN JETON SIGNÉ AVEC UNE AUTRE CLÉ DOIT ÊTRE REFUSÉ.
    ///
    /// C'est le test qui échouerait si l'épinglage d'algorithme
    /// (`ValidAlgorithms = [HmacSha256]`) ou la validation de signature étaient
    /// retirés — deux modifications qui, sinon, ne cassent RIEN de visible.
    /// </summary>
    [Fact]
    public async Task Un_jeton_signe_avec_une_autre_cle_est_refuse()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ForeignToken.Create());

        var response = await client.GetAsync("/api/orders/mine");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Les lectures de catalogue restent ouvertes : une vitrine doit s'afficher sans compte.</summary>
    [Theory]
    [InlineData("/api/catalog/products")]
    [InlineData("/api/food/restaurants")]
    public async Task Les_lectures_de_vitrine_sont_anonymes(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// LECTURE PUBLIQUE N'EST PAS ÉCRITURE PUBLIQUE.
    ///
    /// Les routes `catalog` et `food` sont dédoublées par méthode. Si le
    /// dédoublement disparaissait, n'importe qui pourrait modifier le catalogue
    /// — et la route continuerait de « fonctionner ».
    /// </summary>
    [Theory]
    [InlineData("/api/catalog/products")]
    [InlineData("/api/food/restaurants")]
    public async Task Les_ecritures_de_vitrine_exigent_un_jeton(string route)
    {
        var response = await _factory.CreateClient()
            .PostAsync(route, new StringContent(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>Jeton correctement formé mais signé avec une clé étrangère.</summary>
internal static class ForeignToken
{
    public static string Create()
    {
        const string otherKey = "une-tout-autre-cle-de-32-octets-min!";

        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(otherKey)),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: TestTokens.Issuer,
            audience: TestTokens.Audience,
            claims: [new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
