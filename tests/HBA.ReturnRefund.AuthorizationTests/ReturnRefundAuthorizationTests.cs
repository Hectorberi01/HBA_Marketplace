using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Merchants.Contracts;
using HBA.Tests.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HBA.ReturnRefund.AuthorizationTests;

/// <summary>
/// return-refund-service : arbitrer un retour n'est pas un droit que le rôle
/// `Seller` confère sur les dossiers des autres.
/// </summary>
public sealed class ReturnRefundAuthorizationTests
    : IClassFixture<RetoursSansAppartenanceFactory>
{
    private readonly RetoursSansAppartenanceFactory _factory;

    public ReturnRefundAuthorizationTests(RetoursSansAppartenanceFactory factory) => _factory = factory;

    /// <summary>LA PREMIÈRE BARRIÈRE : L'ACHETEUR N'ENTRE PAS DANS LA SURFACE VENDEUR.</summary>
    [Theory]
    [InlineData("GET", "/api/v1/seller/returns/?page=1&pageSize=20")]
    [InlineData("GET", "/api/v1/seller/returns/{id}")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/approve")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/reject")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/inspection")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/refund-decision")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/shipment")]
    [InlineData("POST", "/api/v1/seller/returns/{id}/receive")]
    public async Task Un_acheteur_n_entre_pas_dans_la_surface_vendeur_des_retours(
        string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// LE CARNET DE RETOURS D'UN CONCURRENT — LE SEUL POINT DE DÉCISION DE CE
    /// SERVICE QUI SE JUGE SANS BASE.
    /// </summary>
    [Fact]
    public async Task Un_vendeur_sans_appartenance_ne_lit_pas_le_carnet_de_retours()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));

        var response = await client.GetAsync("/api/v1/seller/returns/?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>ISSUE-018 : LE `sellerId` DE LA QUERY STRING NE DÉSIGNE PLUS PERSONNE.</summary>
    [Fact]
    public async Task Un_sellerId_dans_la_query_string_ne_rouvre_pas_le_carnet()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));
        var concurrent = Guid.NewGuid();

        var response = await client.GetAsync(
            $"/api/v1/seller/returns/?sellerId={concurrent}&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>L'ARBITRAGE N'EST PAS UNE PRÉROGATIVE DU VENDEUR.</summary>
    [Theory]
    [InlineData("GET", "/api/v1/admin/returns/{id}")]
    [InlineData("POST", "/api/v1/admin/returns/{id}/override")]
    [InlineData("POST", "/api/v1/admin/returns/{id}/close")]
    // LES DEUX CAS `return-policies` ONT ÉTÉ RETIRÉS : LA ROUTE N'EXISTE PLUS.
    public async Task Un_vendeur_n_arbitre_pas_et_ne_fixe_pas_la_politique_de_retour(
        string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>ISSUE-019, CE QUI EN EST VÉRIFIABLE SANS BASE : LE SOCLE.</summary>
    [Theory]
    [InlineData("POST", "/api/v1/marketplace/returns/")]
    [InlineData("GET", "/api/v1/marketplace/returns/?page=1&pageSize=20")]
    [InlineData("GET", "/api/v1/marketplace/returns/{id}")]
    [InlineData("POST", "/api/v1/marketplace/returns/{id}/cancel")]
    [InlineData("POST", "/api/v1/marketplace/returns/{id}/evidence")]
    [InlineData("GET", "/api/v1/marketplace/returns/{id}/timeline")]
    public async Task Aucune_route_client_n_est_atteignable_sans_jeton(string methode, string gabarit)
    {
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(_factory.CreateClient(), methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Le pendant vendeur et administrateur : rien d'ouvert à l'anonyme.</summary>
    [Theory]
    [InlineData("/api/v1/seller/returns/?page=1&pageSize=20")]
    // MÊME ROUTE MORTE, ET CE CAS-CI PASSAIT PAR ACCIDENT.
    public async Task Les_routes_protegees_rendent_401_sans_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Sans `AllowAnonymous`, la `FallbackPolicy` rend 401 sur la sonde, Docker
    /// déclare le conteneur malsain et le redémarre en boucle — sans une seule
    /// erreur applicative dans les journaux.
    /// </summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>L'APPARTENANCE NE SUFFIT PAS : IL FAUT ENCORE LA CAPACITÉ.</summary>
public sealed class MembreSansCapaciteTests : IClassFixture<RetoursMembreSansCapaciteFactory>
{
    private readonly RetoursMembreSansCapaciteFactory _factory;

    public MembreSansCapaciteTests(RetoursMembreSansCapaciteFactory factory) => _factory = factory;

    [Fact]
    public async Task Un_membre_sans_RETURN_VIEW_ne_lit_pas_le_carnet_de_retours()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));

        var response = await client.GetAsync("/api/v1/seller/returns/?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

/// <summary>ET LE PENDANT : CELUI QUI A LE DROIT N'EST PAS ENFERMÉ DEHORS.</summary>
public sealed class MembreAvecCapaciteTests : IClassFixture<RetoursMembreAvecCapaciteFactory>
{
    private readonly RetoursMembreAvecCapaciteFactory _factory;

    public MembreAvecCapaciteTests(RetoursMembreAvecCapaciteFactory factory) => _factory = factory;

    [Fact]
    public async Task Un_membre_qui_porte_RETURN_VIEW_n_est_refoule_ni_en_401_ni_en_403()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));

        var response = await client.GetAsync("/api/v1/seller/returns/?page=1&pageSize=20");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}

// CE QUE CETTE SUITE NE COUVRE PAS, ET QUI RESTE À COUVRIR AILLEURS.

/// <summary>return-refund-service en mémoire, avec un merchant-service simulé.</summary>
public abstract class RetoursFactoryBase : AuthorizationTestFactory<Program>
{
    /// <summary>Ce que le faux merchant-service répond pour l'appelant.</summary>
    protected abstract MerchantAccess? Acces { get; }

    protected override void ConfigureTestDoubles(IServiceCollection services)
    {
        // Le vrai client est un client gRPC vers seller-service : sans
        // substitution, `GetAccessAsync` lèverait sur un port fermé au lieu de
        // refuser, et le test ne distinguerait plus un refus d'une panne.
        var acces = Acces;

        services.RemoveAll<IMerchantAccessApi>();
        services.AddScoped<IMerchantAccessApi>(_ => new AccesVendeurSimule(acces));
    }
}

/// <summary>Un compte rattaché à AUCUNE équipe vendeur.</summary>
public sealed class RetoursSansAppartenanceFactory : RetoursFactoryBase
{
    protected override MerchantAccess? Acces => null;
}

/// <summary>Un membre d'une équipe vendeur, sans la capacité `RETURN_VIEW`.</summary>
public sealed class RetoursMembreSansCapaciteFactory : RetoursFactoryBase
{
    protected override MerchantAccess? Acces => AccesVendeurSimule.Construire("ORDER_VIEW");
}

/// <summary>Un membre d'une équipe vendeur, habilité à lire les retours.</summary>
public sealed class RetoursMembreAvecCapaciteFactory : RetoursFactoryBase
{
    protected override MerchantAccess? Acces => AccesVendeurSimule.Construire("RETURN_VIEW");
}

/// <summary>merchant-service réduit à ce dont l'autorisation des retours dépend.</summary>
internal sealed class AccesVendeurSimule : IMerchantAccessApi
{
    /// <summary>Le seul vendeur que ce faux merchant-service connaisse.</summary>
    public static readonly Guid Vendeur = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly MerchantAccess? _acces;

    public AccesVendeurSimule(MerchantAccess? acces) => _acces = acces;

    public static MerchantAccess Construire(params string[] permissions)
        => new(
            SellerId: Vendeur,
            MemberId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            UserId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            IsOwner: false,
            Permissions: permissions,
            StoreIds: Array.Empty<Guid>(),
            SellerLevelPermissions: permissions,
            PermissionsByStore: new Dictionary<Guid, IReadOnlyList<string>>());

    public Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(_acces);

    public Task<bool> HasCapabilityAsync(
        Guid userId,
        Guid sellerId,
        Guid? storeId,
        string permission,
        CancellationToken cancellationToken = default)
        => Task.FromResult(
            _acces is not null
            && _acces.SellerId == sellerId
            && _acces.CanInStore(storeId, permission));
}
