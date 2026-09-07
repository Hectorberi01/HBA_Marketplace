using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Merchant;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Analytics;
using HBA.Gateway.Application.Contracts.Financial;
using HBA.Gateway.Application.Contracts.Food;
using HBA.Gateway.Application.Contracts.Merchant;
using HBA.Gateway.Application.Contracts.Order;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Merchant BFF — sélecteur d'activités et tableau de bord boutique (§11, §12).</summary>
public sealed class MerchantHandlerTests
{
    private readonly FakeMerchantClient _merchant = new();
    private readonly FakeFoodClient _food = new();
    private readonly FakeOrderClient _order = new();
    private readonly FakeFinancialClient _financial = new();
    private readonly FakeAnalyticsClient _analytics = new();

    private static readonly Guid SellerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StoreId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RestaurantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private GetMerchantActivitiesHandler Activities() => new(_merchant, _food);
    private GetMerchantDashboardHandler Dashboard() => new(_merchant, _order, _financial, _analytics);
    private GetMerchantAnalyticsHandler Analytics() => new(_merchant, _analytics);

    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>La fenêtre exacte que le tableau de bord demande.</summary>
    private static DateOnly Debut(int jours) => Aujourdhui.AddDays(-(jours - 1));

    private void GivenVentes(params (DateOnly Jour, int Commandes, decimal Montant)[] jours)
        => _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Success(
            200,
            Fixtures.Ventes(
                Debut(GetMerchantDashboardHandler.SalesWindowDays), Aujourdhui, jours));

    private void GivenSeller()
        => _merchant.SellerResult = ServiceResult<SellerAccount>.Success(200, Fixtures.Seller(SellerId));

    private static OrderBrief Order(string status, decimal total, DateTime createdUtc)
        => new(Guid.NewGuid(), status, "XOF", total, createdUtc);

    // ─────────────────────────── Sélecteur d'activités ───────────────────────

    [Fact]
    public async Task Le_selecteur_reunit_les_deux_univers_dans_une_seule_liste()
    {
        GivenSeller();
        _merchant.StoresResult = ServiceResult<IReadOnlyList<MerchantStore>>.Success(
            200, [Fixtures.Store(StoreId, SellerId, "HBA Tech Store")]);
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Success(
            200, Fixtures.Partner(RestaurantId));

        var envelope = await Activities().HandleAsync(CancellationToken.None);

        envelope.Data.Activities.Should().HaveCount(2);
        envelope.Data.Activities.Select(a => a.Type).Should()
            .BeEquivalentTo([GetMerchantActivitiesHandler.StoreType, GetMerchantActivitiesHandler.RestaurantType]);
    }

    [Fact]
    public async Task Les_boutiques_sont_demandees_avec_le_sellerId_issu_du_jeton()
    {
        GivenSeller();
        _merchant.StoresResult = ServiceResult<IReadOnlyList<MerchantStore>>.Success(200, []);

        await Activities().HandleAsync(CancellationToken.None);

        // §29/§30 : l'appartenance est portée par le chemin, pas par le corps.
        _merchant.LastSellerId.Should().Be(SellerId);
    }

    [Fact]
    public async Task Un_partenaire_sans_restaurant_ne_voit_que_ses_boutiques()
    {
        GivenSeller();
        _merchant.StoresResult = ServiceResult<IReadOnlyList<MerchantStore>>.Success(
            200, [Fixtures.Store(StoreId, SellerId, "HBA Tech Store")]);
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Failure(404, "aucun établissement");

        var envelope = await Activities().HandleAsync(CancellationToken.None);

        envelope.Data.Activities.Should().ContainSingle()
            .Which.Type.Should().Be(GetMerchantActivitiesHandler.StoreType);

        // Un 404 « je n'ai pas de restaurant » n'est pas une panne : rien à
        // signaler.
        envelope.Warnings.Should().NotContain(w => w.Source == "Food");
    }

    [Fact]
    public async Task Aucune_dependance_n_est_critique_sur_le_premier_ecran()
    {
        // Tout est à terre — l'écran doit tout de même s'afficher, vide et averti,
        // parce qu'un 503 ici enfermerait le partenaire dehors dès la connexion.
        _merchant.SellerResult = ServiceResult<SellerAccount>.Failure(503, "merchant à terre");
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Failure(503, "food à terre");

        var envelope = await Activities().HandleAsync(CancellationToken.None);

        envelope.Data.Activities.Should().BeEmpty();
        envelope.Warnings.Should().Contain(w => w.Source == "Merchant");
    }

    [Fact]
    public async Task Le_role_d_une_boutique_est_deduit_celui_d_un_restaurant_est_lu()
    {
        GivenSeller();
        _merchant.StoresResult = ServiceResult<IReadOnlyList<MerchantStore>>.Success(
            200, [Fixtures.Store(StoreId, SellerId, "HBA Tech Store")]);
        _food.MyRestaurantResult = ServiceResult<PartnerRestaurant>.Success(
            200, Fixtures.Partner(RestaurantId, role: "Manager"));

        var envelope = await Activities().HandleAsync(CancellationToken.None);

        envelope.Data.Activities.Single(a => a.Type == GetMerchantActivitiesHandler.StoreType)
            .Role.Should().Be("OWNER");
        envelope.Data.Activities.Single(a => a.Type == GetMerchantActivitiesHandler.RestaurantType)
            .Role.Should().Be("MANAGER");
    }

    // ─────────────────────────── Tableau de bord ─────────────────────────────

    [Fact]
    public async Task La_boutique_d_un_autre_vendeur_donne_404_et_non_403()
    {
        GivenSeller();
        // Le chemin porte le sellerId du jeton : merchant-service ne trouve pas la
        // boutique sous CE vendeur, donc 404.
        _merchant.StoreResult = ServiceResult<MerchantStore>.Failure(404, "absente");

        var act = () => Dashboard().HandleAsync(StoreId, CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task La_boutique_est_toujours_demandee_sous_le_sellerId_du_jeton()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));

        await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        _merchant.LastSellerId.Should().Be(SellerId);
    }

    /// <summary>LE ROLL-UP FAIT FOI, ET LA LISTE DE COMMANDES NE PEUT PLUS LE CONTREDIRE.</summary>
    [Fact]
    public async Task Les_chiffres_du_jour_viennent_du_roll_up_et_non_de_la_liste_de_commandes()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        GivenVentes((Aujourdhui, 2, 20_000m));
        _order.SellerOrdersResult = ServiceResult<IReadOnlyList<OrderBrief>>.Success(200,
        [
            Order("Paid", 12_000m, DateTime.UtcNow),
            Order("Delivered", 8_000m, DateTime.UtcNow),
            Order("Pending", 500_000m, DateTime.UtcNow),
        ]);

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Today.OrdersToday.Should().Be(2);
        envelope.Data.Today.RevenueToday.Should().Be(20_000m);
        envelope.Data.Today.AverageBasket.Should().Be(10_000m);

        // Celui-ci vient TOUJOURS de la liste : c'est un compte de statuts à
        // l'instant présent, qu'aucun roll-up journalier ne rend.
        envelope.Data.Today.OrdersToProcess.Should().Be(1);
    }

    [Fact]
    public async Task La_fenetre_demandee_a_analytics_est_celle_du_tableau_de_bord()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        GivenVentes();

        await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        _analytics.LastSellerId.Should().Be(SellerId);
        _analytics.LastTo.Should().Be(Aujourdhui);
        _analytics.LastFrom.Should().Be(Debut(GetMerchantDashboardHandler.SalesWindowDays));
    }

    [Fact]
    public async Task Le_tableau_de_bord_porte_la_courbe_sans_trou()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        GivenVentes((Aujourdhui, 2, 20_000m), (Aujourdhui.AddDays(-5), 1, 3_000m));

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Sales.Should().NotBeNull();
        envelope.Data.Sales!.Points.Should().HaveCount(GetMerchantDashboardHandler.SalesWindowDays);
        envelope.Data.Sales.TotalRevenue.Should().Be(23_000m);

        // UN POINT PAR JOUR, DANS L'ORDRE : c'est ce qui empêche une courbe de
        // relier deux jours distants par une droite et de donner à lire une
        // activité continue là où il n'y en a eu aucune.
        envelope.Data.Sales.Points.Should().BeInAscendingOrder(point => point.Day);
    }

    [Fact]
    public async Task Un_jour_sans_vente_donne_un_panier_moyen_nul_et_non_zero()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        GivenVentes();

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        // `0m` s'afficherait « panier moyen : 0 F », ce qui est faux : il n'existe
        // pas.
        envelope.Data.Today.AverageBasket.Should().BeNull();
        envelope.Data.Today.RevenueToday.Should().Be(0m);
    }

    /// <summary>
    /// ANALYTICS À TERRE NE DOIT PAS EMPORTER CE QUE LA PASSERELLE A DÉJÀ OBTENU.
    /// </summary>
    [Fact]
    public async Task Analytics_a_terre_vide_les_chiffres_du_jour_sans_vider_l_ecran()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Failure(503, "analytics à terre");
        _order.SellerOrdersResult = ServiceResult<IReadOnlyList<OrderBrief>>.Success(200,
            [Order("Paid", 12_000m, DateTime.UtcNow)]);

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Sales.Should().BeNull();
        envelope.Data.Today.OrdersToday.Should().BeNull();
        envelope.Data.Today.RevenueToday.Should().BeNull();
        envelope.Data.Today.OrdersToProcess.Should().Be(1);
        envelope.Data.Store.Name.Should().Be("HBA Tech Store");
        envelope.Warnings.Should().Contain(w => w.Source == "Analytics");
    }

    [Fact]
    public async Task Les_commandes_recentes_sont_les_cinq_dernieres_les_plus_neuves_d_abord()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        _order.SellerOrdersResult = ServiceResult<IReadOnlyList<OrderBrief>>.Success(200,
        [
            .. Enumerable.Range(0, 9)
                .Select(i => Order("Delivered", 1_000m, DateTime.UtcNow.AddMinutes(-i))),
        ]);

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.RecentOrders.Should().HaveCount(GetMerchantDashboardHandler.RecentOrderCount);
        envelope.Data.RecentOrders.Should().BeInDescendingOrder(o => o.CreatedAtUtc);
    }

    [Fact]
    public async Task Financial_a_terre_degrade_le_portefeuille_sans_vider_l_ecran()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        _order.SellerOrdersResult = ServiceResult<IReadOnlyList<OrderBrief>>.Success(200,
            [Order("Paid", 12_000m, DateTime.UtcNow)]);
        _financial.SellerWalletResult = ServiceResult<SellerWallet>.Failure(503, "financial à terre");

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Wallet.Should().BeNull();
        envelope.Data.Store.Name.Should().Be("HBA Tech Store");
        envelope.Warnings.Should().Contain(w => w.Source == "Financial");
    }

    /// <summary>LA DEVISE VIENT DE LA SÉRIE, ET LE PORTEFEUILLE N'EST QUE LE REPLI.</summary>
    [Fact]
    public async Task La_devise_vient_de_la_serie_sinon_du_portefeuille()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        GivenVentes();
        _financial.SellerWalletResult = ServiceResult<SellerWallet>.Success(
            200, new SellerWallet(SellerId, 5_000m, 120_000m, 0m, "XOF"));

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Today.Currency.Should().Be("XOF");
        envelope.Data.Wallet!.AvailableBalance.Should().Be(120_000m);
    }

    [Fact]
    public async Task Analytics_muet_laisse_le_portefeuille_donner_la_devise()
    {
        GivenSeller();
        _merchant.StoreResult = ServiceResult<MerchantStore>.Success(
            200, Fixtures.Store(StoreId, SellerId, "HBA Tech Store"));
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Failure(503, "analytics à terre");
        _financial.SellerWalletResult = ServiceResult<SellerWallet>.Success(
            200, new SellerWallet(SellerId, 5_000m, 120_000m, 0m, "XOF"));

        var envelope = await Dashboard().HandleAsync(StoreId, CancellationToken.None);

        envelope.Data.Today.Currency.Should().Be("XOF");
    }

    // ───────────────────────── Écran de courbes dédié ────────────────────────

    [Fact]
    public async Task L_ecran_de_courbes_demande_la_periode_choisie_bornee_a_366_jours()
    {
        GivenSeller();
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Success(
            200, Fixtures.Ventes(Debut(366), Aujourdhui));

        await Analytics().HandleAsync(100_000, CancellationToken.None);

        // `days` VIENT DU CLIENT : sans la borne, il demanderait deux siècles de
        // points, et analytics-service répondrait 400 — un refus que le vendeur ne
        // peut pas comprendre.
        _analytics.LastFrom.Should().Be(Debut(GetMerchantAnalyticsHandler.MaxDays));
        _analytics.LastTo.Should().Be(Aujourdhui);
    }

    [Fact]
    public async Task L_ecran_de_courbes_sans_parametre_prend_trente_jours()
    {
        GivenSeller();
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Success(
            200, Fixtures.Ventes(Debut(30), Aujourdhui));

        await Analytics().HandleAsync(null, CancellationToken.None);

        _analytics.LastFrom.Should().Be(Debut(GetMerchantAnalyticsHandler.DefaultDays));
    }

    /// <summary>UN REFUS DE CAPACITÉ N'EST PAS UNE PANNE D'ÉCRAN.</summary>
    [Fact]
    public async Task Un_membre_sans_la_capacite_voit_l_ecran_sans_la_courbe()
    {
        GivenSeller();
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Failure(403, "capacité manquante");

        var envelope = await Analytics().HandleAsync(null, CancellationToken.None);

        envelope.Data.Sales.Should().BeNull();
        envelope.Warnings.Should().Contain(w => w.Source == "Analytics");
    }

    [Fact]
    public async Task L_ecran_de_courbes_demande_le_vendeur_du_jeton()
    {
        GivenSeller();
        _analytics.SellerSalesResult = ServiceResult<SellerSalesSeries>.Success(
            200, Fixtures.Ventes(Debut(30), Aujourdhui));

        await Analytics().HandleAsync(null, CancellationToken.None);

        // §30 : aucun segment de vendeur ne vient de l'URL sur cette route.
        _analytics.LastSellerId.Should().Be(SellerId);
    }

    [Fact]
    public async Task Sans_dossier_vendeur_le_tableau_de_bord_rend_404()
    {
        _merchant.SellerResult = ServiceResult<SellerAccount>.Failure(404, "pas de dossier");

        var act = () => Dashboard().HandleAsync(StoreId, CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Merchant_a_terre_rend_503_et_non_un_ecran_vide()
    {
        _merchant.SellerResult = ServiceResult<SellerAccount>.Failure(503, "merchant à terre");

        var act = () => Dashboard().HandleAsync(StoreId, CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }
}
