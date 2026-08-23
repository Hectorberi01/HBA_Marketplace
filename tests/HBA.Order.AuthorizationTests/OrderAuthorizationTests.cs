using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HBA.Merchants.Contracts;
using HBA.Shared.Hosting.Http;
using HBA.Tests.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace HBA.Order.AuthorizationTests;

/// <summary>
/// order-service : ce qui gouverne exige un rôle, ce qui appartient exige une
/// preuve de propriété, et les sondes restent anonymes.
/// </summary>
public sealed class OrderAuthorizationTests : IClassFixture<OrderFactory>
{
    private readonly OrderFactory _factory;

    public OrderAuthorizationTests(OrderFactory factory) => _factory = factory;

    /// <summary>
    /// LE PRÉFIXE `/admin` N'A JAMAIS PROTÉGÉ QUOI QUE CE SOIT.
    ///
    /// Ce groupe s'appelait « Admin · Orders » et rendait, à tout compte inscrit,
    /// la liste paginée de TOUTES les commandes de la plateforme — acheteurs,
    /// adresses, montants. Le nom disait admin, la politique disait authentifié.
    /// Ce test échoue si `MapAdminGroup` redevient `MapAuthenticatedGroup`.
    /// </summary>
    [Fact]
    public async Task Un_compte_sans_role_ne_lit_pas_la_liste_admin_des_commandes()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync("/api/admin/orders?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// LE CARNET DE COMMANDES D'UN CONCURRENT.
    ///
    /// `GET /api/sellers/{sellerId}/orders` recopiait le `sellerId` de l'URL sans
    /// jamais lire le jeton : un GUID glané dans une fiche produit suffisait. Le
    /// faux `ISellerModuleApi` ne connaît aucun vendeur pour ce compte —
    /// `ListBySellerAsync` doit donc refuser AVANT d'interroger sa base.
    /// </summary>
    [Fact]
    public async Task Un_compte_qui_n_est_pas_ce_vendeur_ne_lit_pas_ses_commandes()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await client.GetAsync($"/api/sellers/{Guid.NewGuid()}/orders");

        response.StatusCode.Should().BeOneOf(Requetes.RefusOuIntrouvable);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ISSUE-026 — LES CINQ ROUTES VENDEUR SONT GARDÉES, PAS SEULEMENT ÉCRITES.
    ///
    /// CE QUI ÉTAIT CASSÉ : `ORDER_CONFIRM`, `ORDER_REJECT`,
    /// `ORDER_MARK_PREPARING`, `ORDER_MARK_READY` et `ORDER_CANCEL` étaient
    /// déclarées, distribuées au rôle `ORDER_MANAGER`, et ne gardaient AUCUNE
    /// route. Le rôle promettait une autorité qu'il n'exerçait pas.
    ///
    /// LE JETON PORTE LE RÔLE `Seller`, ET C'EST TOUT L'INTÉRÊT DU TEST.
    ///
    /// Sans lui, `MapSellerGroup` refuserait dès la politique du groupe et ce test
    /// ne prouverait rien de plus que le premier. Avec lui, la requête ATTEINT le
    /// gestionnaire — et c'est `DenyUnlessOwnSellerAsync` qui doit refuser, parce
    /// que le faux merchant-service ne rattache aucun vendeur à ce compte.
    ///
    /// Autrement dit : ce test échoue le jour où quelqu'un ajoute une sixième
    /// route de transition dans ce groupe en oubliant sa garde.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Theory]
    [InlineData("confirm")]
    [InlineData("reject")]
    [InlineData("preparing")]
    [InlineData("ready")]
    [InlineData("cancel")]
    public async Task Un_vendeur_ne_pilote_pas_la_commande_d_un_autre(string geste)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create(ApiAuthorization.SellerRole));

        var response = await Requetes.EnvoyerAsync(
            client, "POST", $"/api/sellers/{Guid.NewGuid()}/orders/{Guid.NewGuid()}/{geste}");

        response.StatusCode.Should().BeOneOf(Requetes.RefusOuIntrouvable);
    }

    /// <summary>
    /// CE QUI A ÉTÉ RETIRÉ NE DOIT PAS REVENIR PAR MÉGARDE.
    ///
    /// Trois routes exposaient des transitions de saga — confirmer un paiement,
    /// déclarer une livraison, refuser une commande « au nom du restaurant » — à
    /// qui présentait un jeton. Un 404 prouve qu'aucune n'est routée.
    /// </summary>
    [Theory]
    [InlineData("/payment/confirm")]
    [InlineData("/delivered")]
    [InlineData("/provider/reject")]
    public async Task Les_transitions_de_saga_ne_sont_plus_routees(string suffixe)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create());

        var response = await Requetes.EnvoyerAsync(
            client, "POST", $"/api/orders/{Guid.NewGuid()}{suffixe}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// LA `FallbackPolicy` FERME TOUT CE QUI NE DÉCLARE RIEN — Y COMPRIS LES
    /// SONDES, SI L'ON RETIRE LEUR `AllowAnonymous`.
    ///
    /// Un `/health/live` en 401, et Docker déclare le conteneur malsain puis le
    /// redémarre en boucle, sans une seule erreur applicative dans les journaux.
    /// </summary>
    [Fact]
    public async Task La_sonde_de_vie_repond_en_anonyme()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Sans jeton, les routes de l'acheteur restent fermées.</summary>
    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/admin/orders")]
    public async Task Les_routes_protegees_rendent_401_sans_jeton(string route)
    {
        var response = await _factory.CreateClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>order-service en mémoire, avec un merchant-service qui ne reconnaît personne.</summary>
public sealed class OrderFactory : AuthorizationTestFactory<Program>
{
    protected override void ConfigureTestDoubles(IServiceCollection services)
    {
        // Le vrai client est un client gRPC vers merchant-service : sans
        // substitution, `ListBySellerAsync` lèverait au lieu de refuser, et le
        // test ne distinguerait plus un refus d'une panne.
        services.RemoveAll<ISellerModuleApi>();
        services.AddScoped<ISellerModuleApi, VendeurInconnu>();

        // `IMerchantAccessApi` AUSSI, ET PAS SEULEMENT `ISellerModuleApi`.
        //
        // Depuis le lot D1, la garde ne demande plus « quel est le vendeur de ce
        // compte » mais « ce compte a-t-il cette capacité ». Les deux contrats sont
        // servis par le MÊME client gRPC, et l'enregistrement de `IMerchantAccessApi`
        // le résout par un cast depuis `ISellerModuleApi` : substituer l'un sans
        // l'autre fait lever un `InvalidCastException` à la première requête, et le
        // test rendrait 500 là où il doit distinguer un refus.
        services.RemoveAll<IMerchantAccessApi>();
        services.AddScoped<IMerchantAccessApi, AucuneCapacite>();
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

    /// <summary>
    /// Vendeur inconnu — et c'est `Unknown`, pas `NotConfigured`.
    ///
    /// La distinction est tout l'objet de <see cref="SellerPayout"/> : servir
    /// « aucun compte configuré » à un identifiant qui ne désigne personne est ce
    /// qui rendait le défaut du retrait vendeur illisible pour le support.
    /// </summary>
    public Task<SellerPayout> GetSellerPayoutAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => Task.FromResult(SellerPayout.Unknown);
}

/// <summary>
/// merchant-service qui ne reconnaît aucune appartenance — donc aucune capacité.
/// </summary>
/// <remarks>
/// IL REND `null` ET NON UN ACCÈS VIDE.
///
/// Un `MerchantAccess` sans permission passerait la résolution et échouerait à la
/// capacité : le test verrait un 403 « rôle insuffisant » là où le cas éprouvé est
/// « ce compte n'est rattaché à personne ». Les deux refus se ressemblent en HTTP
/// et ne disent pas la même chose.
/// </remarks>
internal sealed class AucuneCapacite : IMerchantAccessApi
{
    public Task<MerchantAccess?> GetAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<MerchantAccess?>(null);

    public Task<bool> HasCapabilityAsync(
        Guid userId, Guid sellerId, Guid? storeId, string permission,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
