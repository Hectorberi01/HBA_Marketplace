using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace HBA.Gateway.IntegrationTests;

public sealed class RoutingTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public RoutingTests(GatewayFactory factory) => _factory = factory;

    /// <summary>
    /// LE TEST QUI DÉTECTE UN CLUSTER SANS ADRESSE.
    ///
    /// Les clusters ne portent aucune adresse dans `appsettings.json` : c'est
    /// `ServiceAddressConfigFilter` qui les renseigne depuis la section
    /// `Services`. Une faute de frappe dans un nom de cluster — `Catalogue` au
    /// lieu de `Catalog` — laisse le cluster SANS destination. Rien n'échoue au
    /// démarrage : les requêtes rendent simplement 503, et seule une lecture
    /// attentive des journaux le révèle.
    /// </summary>
    [Fact]
    public void Chaque_cluster_recoit_une_destination_depuis_la_section_Services()
    {
        using var scope = _factory.Services.CreateScope();
        var proxy = scope.ServiceProvider.GetRequiredService<IProxyStateLookup>();

        var clusters = proxy.GetClusters().ToArray();

        clusters.Should().NotBeEmpty("la section ReverseProxy doit être chargée");

        var orphans = clusters
            .Where(cluster => (cluster.Model.Config.Destinations?.Count ?? 0) == 0)
            .Select(cluster => cluster.ClusterId)
            .ToArray();

        orphans.Should().BeEmpty(
            "chaque ClusterId doit correspondre à une clé de la section Services");
    }

    /// <summary>
    /// Les quinze préfixes publics exigés au §5 sont tous routés. Un préfixe
    /// oublié ne produirait pas d'erreur : la requête tomberait simplement en 404,
    /// et le diagnostic partirait du côté du service au lieu de la passerelle.
    /// </summary>
    [Fact]
    public void Les_quinze_prefixes_publics_sont_couverts()
    {
        using var scope = _factory.Services.CreateScope();
        var proxy = scope.ServiceProvider.GetRequiredService<IProxyStateLookup>();

        var paths = proxy.GetRoutes()
            .Select(route => route.Config.Match.Path)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToArray();

        string[] expected =
        [
            "/api/auth/", "/api/users/", "/api/merchants/", "/api/catalog/", "/api/inventory/",
            "/api/cart/", "/api/wishlist/", "/api/orders/", "/api/food/", "/api/delivery/",
            "/api/payments/", "/api/wallet/", "/api/reviews/", "/api/notifications/", "/api/media/"
        ];

        foreach (var prefix in expected)
        {
            paths.Should().Contain(path => path.StartsWith(prefix, StringComparison.Ordinal),
                $"le préfixe public {prefix}* doit être routé");
        }
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// TOUTE ROUTE DONT LE NOM FINIT PAR `-legacy` DOIT PORTER UN `PathPattern`.
    ///
    /// LE DÉFAUT QUE CE TEST ATTRAPE EST UNE PANNE TOTALE, ET ELLE EST MUETTE.
    ///
    /// Quand un service passe à `/api/v1/...`, l'ancien chemin reste routé pour ne
    /// pas casser les applications déjà installées — mais il ne suffit PAS de le
    /// laisser routé : sans réécriture, la passerelle transmet `/api/catalog/...`
    /// à un service qui ne sert plus que `/api/v1/catalog/...`. Le cluster répond,
    /// la passerelle est contente, et le client reçoit 404 sur toute la surface.
    ///
    /// Rien ne le signale : le test des quinze préfixes reste VERT, puisque le
    /// préfixe est bien routé. C'est le `Transform` qui manque, pas la route — et
    /// c'est précisément ce que ce test regarde.
    ///
    /// Le jour où une coquille est retirée (télémétrie à zéro), la route disparaît
    /// avec elle et ce test n'a plus rien à vérifier. Il ne coûte donc rien à
    /// garder.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public void Chaque_coquille_de_depreciation_reecrit_vers_le_chemin_versionne()
    {
        using var scope = _factory.Services.CreateScope();
        var proxy = scope.ServiceProvider.GetRequiredService<IProxyStateLookup>();

        var coquilles = proxy.GetRoutes()
            .Select(route => route.Config)
            .Where(config => config.RouteId.EndsWith("-legacy", StringComparison.Ordinal))
            .ToArray();

        coquilles.Should().NotBeEmpty(
            "au moins un service a migré vers /api/v1/ et doit garder son ancien chemin");

        var sansReecriture = coquilles
            .Where(config => config.Transforms is null
                || !config.Transforms.Any(t => t.ContainsKey("PathPattern")))
            .Select(config => config.RouteId)
            .ToArray();

        sansReecriture.Should().BeEmpty(
            "une coquille de dépréciation sans PathPattern transmet l'ancien chemin tel quel "
            + "à un service qui ne le sert plus, et rend 404 sur toute la surface");
    }
}
