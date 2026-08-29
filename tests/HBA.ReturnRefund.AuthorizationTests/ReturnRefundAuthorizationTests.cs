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
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES TROIS PANNES QUE CE FICHIER EMPÊCHE DE REVENIR.
///
///   • ISSUE-017 — les huit routes de `/api/v1/seller/returns` exigeaient le rôle
///     `Seller` et RIEN D'AUTRE. Tout vendeur inscrit approuvait, rejetait,
///     inspectait et surtout CHIFFRAIT LE REMBOURSEMENT du dossier d'un
///     concurrent, avec un identifiant de retour pour seule clé.
///   • ISSUE-018 — la liste liait son `sellerId` depuis la QUERY STRING : le
///     groupe ne porte aucun `{sellerId}`, et `GET /api/v1/seller/returns/
///     ?sellerId=…` rendait le carnet de retours complet de n'importe quel
///     vendeur. Une fuite commerciale en une requête, sans outil.
///   • ISSUE-019 — côté client, l'identité de l'appelant n'était pas transmise à
///     la commande : ouvrir un retour au nom d'un tiers, lire son dossier et sa
///     chronologie ne demandait qu'un identifiant glané dans un ticket.
///
/// ET CE QUE CE FICHIER NE COUVRE PAS — VOIR L'ENCADRÉ « CE QUE CETTE
/// SUITE NE COUVRE PAS », PLUS BAS.
/// Sept des huit routes vendeur, et la totalité de la garde de propriété client,
/// lisent le dossier AVANT de décider. Sans base, elles ne peuvent pas être
/// éprouvées ici, et prétendre le contraire écrirait des tests qui passent pour
/// de mauvaises raisons.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReturnRefundAuthorizationTests
    : IClassFixture<RetoursSansAppartenanceFactory>
{
    private readonly RetoursSansAppartenanceFactory _factory;

    public ReturnRefundAuthorizationTests(RetoursSansAppartenanceFactory factory) => _factory = factory;

    /// <summary>
    /// LA PREMIÈRE BARRIÈRE : L'ACHETEUR N'ENTRE PAS DANS LA SURFACE VENDEUR.
    ///
    /// `MapSellerGroup` exige `Seller`, `Admin` ou `Moderator`. Ce test tombe le
    /// jour où quelqu'un « aligne » ce groupe sur `MapAuthenticatedGroup` en
    /// pensant que les gardes d'appartenance suffisent — elles ne suffisaient
    /// justement pas, c'est tout l'objet d'ISSUE-017.
    ///
    /// 403 et non 404 : c'est le RÔLE qui manque, pas la ressource. Refuser sur
    /// le rôle n'apprend à personne si ce dossier de retour existe.
    /// </summary>
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE CARNET DE RETOURS D'UN CONCURRENT — LE SEUL POINT DE DÉCISION DE
    ///    CE SERVICE QUI SE JUGE SANS BASE.
    ///
    /// `ListAsync` demande `GetAccessAsync(userId)` AVANT toute requête SQL : le
    /// vendeur n'est plus lu dans l'URL, il est résolu depuis le jeton. Le faux
    /// merchant-service ne rattache ce compte à aucune équipe, la route doit donc
    /// refuser sans avoir rien lu — et c'est vérifiable ici, alors que PostgreSQL
    /// est absent.
    ///
    /// Le compte porte pourtant le rôle `Seller` : c'est un VRAI vendeur, il a
    /// franchi le groupe. Le refus vient de l'appartenance, pas du rôle — c'est la
    /// distinction qu'ISSUE-017 avait laissée béante.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Un_vendeur_sans_appartenance_ne_lit_pas_le_carnet_de_retours()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));

        var response = await client.GetAsync("/api/v1/seller/returns/?page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// ISSUE-018 : LE `sellerId` DE LA QUERY STRING NE DÉSIGNE PLUS PERSONNE.
    ///
    /// C'est le test de non-régression le plus direct de la vague. Avant, le
    /// paramètre était LIÉ à la signature du handler et servait tel quel de filtre :
    /// `?sellerId=<le GUID d'un concurrent>` rendait son carnet complet. Il a
    /// disparu de la signature — donc il n'est plus qu'une chaîne inerte dans
    /// l'URL, et la réponse doit être exactement la même que sans lui : un refus.
    ///
    /// Ce test échoue le jour où quelqu'un « rétablit » le paramètre pour dépanner
    /// un écran d'administration. Le bon endroit pour cela est
    /// `/api/v1/admin/returns`, pas la surface vendeur.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Un_sellerId_dans_la_query_string_ne_rouvre_pas_le_carnet()
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));
        var concurrent = Guid.NewGuid();

        var response = await client.GetAsync(
            $"/api/v1/seller/returns/?sellerId={concurrent}&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// L'ARBITRAGE N'EST PAS UNE PRÉROGATIVE DU VENDEUR.
    ///
    /// `/api/v1/admin/returns/{id}/override` passe outre la machine à états, et
    /// `/close` clôt le dossier : ce sont les gestes qui tranchent un litige
    /// CONTRE une des deux parties. Les ouvrir au rôle `Seller` reviendrait à
    /// laisser le vendeur arbitrer son propre différend.
    ///
    /// Le jeton porte `Seller` et non un compte nu : le test doit distinguer
    /// « pas de rôle du tout » de « le mauvais rôle ». C'est le second cas qui
    /// se rouvre par distraction, en ajoutant `SellerRole` à `MapAdminGroup`
    /// pour dépanner un écran partenaire.
    /// </summary>
    [Theory]
    [InlineData("GET", "/api/v1/admin/returns/{id}")]
    [InlineData("POST", "/api/v1/admin/returns/{id}/override")]
    [InlineData("POST", "/api/v1/admin/returns/{id}/close")]
    // ═══════════════════════════════════════════════════════════════════════════
    // LES DEUX CAS `return-policies` ONT ÉTÉ RETIRÉS : LA ROUTE N'EXISTE PLUS.
    //
    // `MapReturnPolicyEndpoints` a été supprimé de `Program.cs` — il répondait et
    // n'écrivait rien. Le test, lui, n'a pas suivi : il exigeait 403 sur un chemin
    // que plus personne ne mappe, et recevait 404.
    //
    // Ce n'est pas le test qui avait tort sur le FOND — un vendeur ne doit pas
    // fixer la politique de retour, et ça reste vrai. C'est qu'il éprouvait une
    // route morte. Le jour où la politique de retour reviendra, ces deux lignes
    // seront à remettre EN MÊME TEMPS que le mapping.
    // ═══════════════════════════════════════════════════════════════════════════
    public async Task Un_vendeur_n_arbitre_pas_et_ne_fixe_pas_la_politique_de_retour(
        string methode, string gabarit)
    {
        var client = _factory.CreateClientWithToken(TestTokens.Create("Seller"));
        var route = gabarit.Replace("{id}", Guid.NewGuid().ToString());

        var response = await Requetes.EnvoyerAsync(client, methode, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// ISSUE-019, CE QUI EN EST VÉRIFIABLE SANS BASE : LE SOCLE.
    ///
    /// Les six routes client vivent dans `MapAuthenticatedGroup`. Tant qu'elles y
    /// sont, aucune ne peut être atteinte sans jeton — et c'est la condition
    /// nécessaire pour que `CurrentUserId` ait quelque chose à lire. Le jour où
    /// l'une d'elles reçoit un `AllowAnonymous` « pour le partage de lien », la
    /// garde de propriété qui suit ne compare plus rien : `CurrentUserId` rend
    /// `null`, et c'est ce `null` que `CreateReturnCommand` refuse en dernier
    /// recours.
    ///
    /// Ce test tient donc le premier maillon, pas la garde elle-même. Voir
    /// l'encadré « CE QUE CETTE SUITE NE COUVRE PAS » pour ce qui manque.
    /// </summary>
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
    //
    // Sans jeton, un chemin non routé rend 401 par la `FallbackPolicy` — le même
    // code que celui qu'attendait le test. Il était donc vert tout en n'éprouvant
    // plus rien : il ne distinguait pas « route protégée » de « route absente ».
    // Un test vert qui n'éprouve rien est pire qu'un test rouge.
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

/// <summary>
/// L'APPARTENANCE NE SUFFIT PAS : IL FAUT ENCORE LA CAPACITÉ.
/// </summary>
/// <remarks>
/// « DEUX CONTRÔLES, PAS UN » N'EST PAS UNE FORMULE, C'EST CE QUI SÉPARE UN
/// GESTIONNAIRE DE COMMANDES D'UN GÉRANT.
///
/// Ce compte appartient bel et bien à une équipe vendeur — `GetAccessAsync` rend
/// un `MerchantAccess` — mais ses permissions ne portent pas `RETURN_VIEW`. Sans
/// la seconde vérification, tout membre d'une équipe, y compris un préparateur de
/// colis, lirait le carnet de retours et ses montants.
///
/// Une classe de test distincte parce que la substitution est posée à la
/// construction de l'hôte : deux comportements de merchant-service exigent deux
/// hôtes.
/// </remarks>
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

/// <summary>
/// ET LE PENDANT : CELUI QUI A LE DROIT N'EST PAS ENFERMÉ DEHORS.
/// </summary>
/// <remarks>
/// C'EST LE VRAI RISQUE D'UNE GARDE AJOUTÉE APRÈS COUP, ET IL S'EST DÉJÀ
/// PRODUIT DANS CE DÉPÔT.
///
/// Une permission mal orthographiée — `RETURN_VIEWS`, `RETURNS_VIEW` — ne casse
/// rien à la compilation : elle rend simplement `Can` faux pour tout le monde, et
/// la route devient inaccessible à ses propres ayants droit. Personne ne s'en
/// aperçoit tant qu'aucun test ne demande à un compte LÉGITIME de passer.
///
/// L'ASSERTION EST « NI 401 NI 403 », PAS « 200 ». Une fois la capacité
/// franchie, `ListAsync` interroge sa base, qui n'existe pas ici : la réponse est
/// une erreur serveur, et c'est précisément la PREUVE que le contrôle a été
/// franchi. Voir l'encadré « COROLLAIRE » d'`AuthorizationTestFactory`.
/// </remarks>
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

// ═════════════════════════════════════════════════════════════════════════════
// CE QUE CETTE SUITE NE COUVRE PAS, ET QUI RESTE À COUVRIR AILLEURS.
//
// Ce n'est pas une réserve de principe : ce sont les gardes centrales
// d'ISSUE-017 et d'ISSUE-019, et elles sont HORS DE PORTÉE d'un test sans base.
//
// ── 1. `VerifierAsync` sur SEPT des huit routes vendeur ──────────────────────
//
// `GetAsync` et `ExecuterAsync` commencent tous deux par
// `sender.Send(new GetReturnQuery(id))` — c'est-à-dire `IReturnRequestRepository
// .GetAsync`, donc EF Core, donc PostgreSQL — AVANT d'appeler `VerifierAsync`.
// C'est délibéré et c'est bien écrit dans le fichier : « le vendeur d'un retour
// n'est pas dans le jeton, il est dans la ressource ». Mais la conséquence pour
// nous est nette : sans base, la requête n'atteint JAMAIS
// `HasCapabilityAsync`. Substituer merchant-service ne change rien, et un test
// qui affirmerait ici « 403 » ou « pas 403 » lirait le comportement de la panne
// de base, pas celui de la garde.
//
// Ne sont donc PAS éprouvés : `GET /{id}`, `/approve`, `/reject`,
// `/inspection`, `/refund-decision`, `/shipment`, `/receive` — c'est-à-dire le
// cœur d'ISSUE-017, y compris la route qui CHIFFRE le remboursement.
//
// ── 2. La garde de propriété client, en entier ───────────────────────────────
//
// `EstLeSien` compare `dossier.CustomerId` à l'appelant, et
// `VerifierProprietaireAsync` charge le dossier pour cela. Même obstacle. Le
// choix de rendre 404 plutôt que 403 — pour ne pas confirmer l'existence du
// dossier à un inconnu — n'est donc pas vérifié non plus.
//
// ── 3. `CreateReturnCommand.RequestedByUserId` ───────────────────────────────
//
// Le handler refuse un demandeur nul, puis compare l'appelant au `CustomerId`
// rendu par order-service. Deux dépendances manquent : la lecture par clé
// d'idempotence (base) et `IOrderGrpcClient` (voisin). Le refus
// « return.order_not_found » sur la commande d'un tiers, comme le refus
// « return.idempotency_conflict », restent non éprouvés.
//
// ── CE QU'IL FAUDRAIT ────────────────────────────────────────────────────────
//
// Un projet `tests/HBA.ReturnRefund.IntegrationTests`, sur le modèle de
// `tests/HBA.Catalog.IntegrationTests` : conteneurs Postgres et Kafka,
// `Database__MigrateOnStartup=true` sur base vide, un dossier de retour semé
// pour le vendeur A et le client X, puis les mêmes routes appelées avec le jeton
// du vendeur B et celui du client Y. C'est le seul niveau où « ce dossier ne
// vous appartient pas » devient observable.
//
// TANT QUE CE PROJET N'EXISTE PAS, LA VAGUE 1 N'EST PAS COUVERTE PAR DES
// TESTS — elle est couverte par une RELECTURE. Les tests ci-dessus tiennent les
// deux barrières périphériques (le rôle du groupe, et la résolution du vendeur
// depuis le jeton sur la liste) ; ils ne tiennent pas la garde d'appartenance
// elle-même.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// return-refund-service en mémoire, avec un merchant-service simulé.
/// </summary>
/// <remarks>
/// SEUL `IMerchantAccessApi` EST SUBSTITUÉ ICI — PAS `ISellerModuleApi`.
///
/// Les suites d'order-service et de financial-service remplacent LES DEUX, et
/// c'est nécessaire chez elles : leurs gardes résolvent encore le vendeur par
/// `ISellerModuleApi`, et l'enregistrement d'`IMerchantAccessApi` le produit PAR
/// UN CAST depuis cette même instance (voir `MerchantsGrpcRegistration`) —
/// substituer l'un sans l'autre y lève un `InvalidCastException`.
///
/// Ici, aucune route ne touche `ISellerModuleApi` : les endpoints vendeur ne
/// prennent que `IMerchantAccessApi`. Remplacer l'enregistrement d'arrivée suffit,
/// et le cast d'origine n'est jamais exécuté. Poser un faux `ISellerModuleApi` de
/// six méthodes en plus laisserait croire que ce service en dépend.
/// </remarks>
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
/// <remarks>
/// `null` ET NON UN ACCÈS VIDE. Les deux refusent, ils ne disent pas la même
/// chose : `null` signifie « ce compte n'a aucun dossier vendeur », un
/// `MerchantAccess` sans permission signifie « il est de l'équipe, mais pas
/// habilité ». C'est le premier cas que ce fichier éprouve ici, et le second dans
/// `RetoursMembreSansCapaciteFactory`.
/// </remarks>
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

/// <summary>
/// merchant-service réduit à ce dont l'autorisation des retours dépend.
/// </summary>
/// <remarks>
/// `HasCapabilityAsync` VÉRIFIE AUSSI LE `sellerId`, ALORS QU'AUCUN TEST NE
/// L'ATTEINT AUJOURD'HUI.
///
/// Les sept routes qui l'appellent lisent leur dossier avant, donc leur base,
/// donc échouent ici (voir l'encadré « CE QUE CETTE SUITE NE COUVRE PAS »). Ce faux est écrit
/// juste quand même : le jour où un test d'intégration sème un dossier, un faux
/// qui rendrait `true` sans regarder le vendeur transformerait ce test en
/// approbation automatique — exactement le défaut que ces suites reprochent au
/// dépôt.
/// </remarks>
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
