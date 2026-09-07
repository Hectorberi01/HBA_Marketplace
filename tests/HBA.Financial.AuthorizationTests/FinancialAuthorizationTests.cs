using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Deliveries.Contracts;
using HBA.Merchants.Contracts;
using HBA.Tests.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HBA.Financial.AuthorizationTests;

/// <summary>financial-service : le service de l'ARGENT ne demandait qu'un compte.</summary>
public sealed class FinancialAuthorizationTests : IClassFixture<FinancialFactory>
{
    private readonly FinancialFactory _factory;

    public FinancialAuthorizationTests(FinancialFactory factory) => _factory = factory;

    /// <summary>
    /// Chaque ligne correspond à un geste qui déplace de l'argent ou fixe ce que la
    /// plateforme prélève.
    /// </summary>
    [Theory]
    [InlineData("POST", "/api/financial/payments/{id}/refund")]
    [InlineData("GET", "/api/financial/payments/?page=1&pageSize=20")]
    [InlineData("POST", "/api/financial/commissions/")]
    [InlineData("PUT", "/api/financial/commissions/{id}")]
    [InlineData("POST", "/api/financial/wallets/withdrawals/{id}/approve")]
    [InlineData("POST", "/api/financial/wallets/withdrawals/{id}/reject")]
    [InlineData("GET", "/api/financial/wallets/withdrawals/pending")]
    [InlineData("GET", "/api/financial/wallets/platform")]
    [InlineData("POST", "/api/financial/settlements/")]
    [InlineData("GET", "/api/financial/settlements/")]
    public async Task Un_compte_sans_role_ne_gouverne_pas_l_argent(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// LE `sellerId` VENAIT DE L'URL ET PERSONNE NE LE CONFRONTAIT AU JETON.
    ///
    /// Le portefeuille, le relevé et les factures de n'importe quel vendeur — et
    /// surtout la demande de retrait, qui déplace un solde vers un compte
    /// bancaire. Le faux merchant-service ne rattache aucun vendeur à l'appelant :
    /// `EnsureSellerAsync` doit refuser avant que la moindre requête ne parte.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/financial/wallets/sellers/{id}")]
    [InlineData("GET", "/api/financial/wallets/sellers/{id}/transactions?take=10")]
    [InlineData("GET", "/api/financial/wallets/sellers/{id}/withdrawals")]
    [InlineData("POST", "/api/financial/wallets/sellers/{id}/withdrawals")]
    [InlineData("GET", "/api/financial/invoices/seller/{id}")]
    [InlineData("GET", "/api/financial/settlements/sellers/{id}/payouts")]
    public async Task Un_compte_qui_n_est_pas_ce_vendeur_n_atteint_pas_son_argent(string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().BeOneOf(Requetes.RefusOuIntrouvable);
    }

    /// <summary>
    /// LE SOLDE ET LES GAINS D'UN LIVREUR, POUR QUI CONNAÎT SON `driverId`.
    ///
    /// La route est authentifiée sans rôle : sans `EnsureDriverAsync`, un
    /// identifiant de livreur suffisait. Le faux delivery-service rattache
    /// `DriverConnu` à `UtilisateurDuLivreur` et à personne d'autre.
    /// </summary>
    [Theory]
    [InlineData("/api/financial/wallets/drivers/{id}")]
    [InlineData("/api/financial/wallets/drivers/{id}/transactions?take=10")]
    public async Task Un_autre_compte_ne_lit_pas_le_portefeuille_du_livreur(string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());
        var route = gabarit.Replace("{id}", FinancialFactory.DriverConnu.ToString());

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Un `driverId` inconnu ne dit pas non plus s'il existe : on refuse.</summary>
    [Fact]
    public async Task Un_livreur_inconnu_est_refuse_et_non_confirme()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync($"/api/financial/wallets/drivers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>LE TEST DE LA RÉGRESSION QU'ON VIENT DE RÉPARER.</summary>
    [Theory]
    [InlineData("/api/financial/wallets/drivers/{id}")]
    [InlineData("/api/financial/wallets/drivers/{id}/transactions?take=10")]
    public async Task Le_livreur_concerne_franchit_l_autorisation(string gabarit)
    {
        var client = _factory.CreateClientWithToken(
            TestTokens.Create(FinancialFactory.UtilisateurDuLivreur));
        var route = gabarit.Replace("{id}", FinancialFactory.DriverConnu.ToString());

        var response = await client.GetAsync(route);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>LE WEBHOOK PSP DOIT RESTER ANONYME — SA SERRURE EST LA SIGNATURE.</summary>
    [Fact]
    public async Task Le_webhook_du_prestataire_reste_anonyme()
    {
        var response = await Requetes.EnvoyerAsync(
            _factory.CreateClient(), "POST", "/api/financial/payments/webhooks/stripe");

        response.Headers.WwwAuthenticate.Should().BeEmpty(
            "le pipeline n'a pas dû défier l'appelant : la route est anonyme, "
            + "sa serrure est la signature du prestataire");
    }

    /// <summary>
    /// Le pendant du test précédent : une route voisine du même service, elle, DOIT
    /// défier.
    /// </summary>
    [Fact]
    public async Task Une_route_protegee_defie_bien_l_appelant()
    {
        var response = await _factory.CreateClient().GetAsync("/api/financial/payment-methods/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
    }

    /// <summary>Sans `AllowAnonymous`, Docker redémarre le conteneur en boucle.</summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/financial/payment-methods/")]
    [InlineData("/api/financial/commissions/")]
    public async Task Les_routes_protegees_rendent_401_sans_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>financial-service en mémoire, avec ses deux voisins d'autorisation simulés.</summary>
public sealed class FinancialFactory : AuthorizationTestFactory<Program>
{
    /// <summary>Le seul livreur que le faux delivery-service connaisse.</summary>
    public static readonly Guid DriverConnu = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Le compte utilisateur auquel ce livreur est rattaché.</summary>
    public static readonly Guid UtilisateurDuLivreur = Guid.Parse("22222222-2222-2222-2222-222222222222");

    protected override void ConfigureTestDoubles(IServiceCollection services)
    {
        // Ces deux clients ne servent QU'À L'AUTORISATION : `EnsureSellerAsync` et
        // `EnsureDriverAsync` les interrogent avant toute lecture de la base. Les
        // laisser pointer vers un port fermé ferait lever au lieu de refuser, et
        // un 500 ne prouve rien sur la décision d'autorisation.
        services.RemoveAll<ISellerModuleApi>();
        services.AddScoped<ISellerModuleApi, VendeurInconnu>();

        // `IMerchantAccessApi` AUSSI, ET PAS SEULEMENT `ISellerModuleApi`.
        services.RemoveAll<IMerchantAccessApi>();
        services.AddScoped<IMerchantAccessApi, AucuneCapacite>();

        services.RemoveAll<IDeliveryModuleApi>();
        services.AddScoped<IDeliveryModuleApi, UnSeulLivreur>();
    }
}

/// <summary>merchant-service qui ne rattache aucun vendeur au compte appelant.</summary>
internal sealed class VendeurInconnu : ISellerModuleApi
{
    public Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult<SellerSummary?>(null);

    public Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<SellerSummary?>(null);

    public Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<StoreSummary?> GetStoreAsync(Guid storeId, CancellationToken cancellationToken = default)
        => Task.FromResult<StoreSummary?>(null);

    public Task<IReadOnlyList<StoreSummary>> ListStoresBySellerAsync(
        Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StoreSummary>>([]);

    /// <summary>Vendeur inconnu — et c'est `Unknown`, pas `NotConfigured`.</summary>
    public Task<SellerPayout> GetSellerPayoutAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult(SellerPayout.Unknown);
}

/// <summary>delivery-service qui ne connaît qu'un livreur, rattaché à un seul compte.</summary>
internal sealed class UnSeulLivreur : IDeliveryModuleApi
{
    public Task<DeliverySummary?> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default)
        => Task.FromResult<DeliverySummary?>(null);

    public Task<DriverAccount?> GetDriverAccountAsync(Guid driverId, CancellationToken cancellationToken = default)
        => Task.FromResult(driverId == FinancialFactory.DriverConnu
            ? new DriverAccount(driverId, FinancialFactory.UtilisateurDuLivreur, "Kofi Ahouangan")
            : null);

    public Task<DeliverySummary?> GetByReferenceAsync(
        string reference, string source, CancellationToken cancellationToken = default)
        => Task.FromResult<DeliverySummary?>(null);

    public Task<DeliveryTracking?> GetTrackingAsync(Guid deliveryId, CancellationToken cancellationToken = default)
        => Task.FromResult<DeliveryTracking?>(null);
}

/// <summary>merchant-service qui ne reconnaît aucune appartenance — donc aucune capacité.</summary>
internal sealed class AucuneCapacite : IMerchantAccessApi
{
    public Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<MerchantAccess?>(null);

    public Task<bool> HasCapabilityAsync(
        Guid userId, Guid sellerId, Guid? storeId, string permission,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
