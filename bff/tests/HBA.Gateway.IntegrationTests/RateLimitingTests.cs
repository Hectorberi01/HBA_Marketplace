using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HBA.Gateway.IntegrationTests;

public sealed class RateLimitingTests
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE TEST QUI PROTÈGE `/api/auth/login` DE L'ÉNUMÉRATION.
    ///
    /// Sans limite sur cette route, un attaquant essaie des mots de passe aussi
    /// vite que le réseau le permet. La limite est basse EXPRÈS — dix par minute
    /// gêne à peine un humain et arrête net un script.
    ///
    /// La limite est abaissée à 3 pour ce test : la laisser à 10 exigerait onze
    /// requêtes réelles, chacune tentant d'atteindre identity-service et attendant
    /// son délai de connexion.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task La_route_de_connexion_finit_par_rendre_429()
    {
        await using var factory = new ConfigurableGatewayFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:PermitLimit"] = "3",
            ["RateLimiting:Auth:WindowSeconds"] = "60"
        });

        var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsync("/api/auth/login", new StringContent(string.Empty));
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Un refus doit avoir la MÊME forme que toute autre erreur de la passerelle.
    /// Une application cliente qui sait lire un ProblemDetails sait alors lire un
    /// 429 sans code supplémentaire.
    /// </summary>
    [Fact]
    public async Task Le_refus_est_un_ProblemDetails_avec_Retry_After()
    {
        await using var factory = new ConfigurableGatewayFactory(new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:PermitLimit"] = "1",
            ["RateLimiting:Auth:WindowSeconds"] = "60"
        });

        var client = factory.CreateClient();

        HttpResponseMessage? rejected = null;

        for (var attempt = 0; attempt < 4 && rejected is null; attempt++)
        {
            var response = await client.PostAsync("/api/auth/login", new StringContent(string.Empty));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
            }
        }

        rejected.Should().NotBeNull("la limite de 1 requête doit être atteinte en 4 essais");
        rejected!.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        rejected.Headers.Contains("Retry-After").Should().BeTrue();
    }

    /// <summary>
    /// La lecture du catalogue doit rester large : limiter tôt casse le
    /// défilement d'une liste de produits, ce qu'aucun attaquant ne cherche.
    /// </summary>
    [Fact]
    public async Task La_lecture_du_catalogue_supporte_une_rafale_normale()
    {
        await using var factory = new ConfigurableGatewayFactory([]);
        var client = factory.CreateClient();

        for (var attempt = 0; attempt < 15; attempt++)
        {
            var response = await client.GetAsync("/api/catalog/products");
            response.StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }
}

/// <summary>Variante de <see cref="GatewayFactory"/> à configuration ajustable.</summary>
internal sealed class ConfigurableGatewayFactory : GatewayFactory
{
    private readonly Dictionary<string, string?> _overrides;

    public ConfigurableGatewayFactory(Dictionary<string, string?> overrides)
        => _overrides = overrides;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Ajouté APRÈS la base : la dernière source ajoutée l'emporte.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_overrides));
    }
}
