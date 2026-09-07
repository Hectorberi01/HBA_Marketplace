using System.Reflection;
using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Driver;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Delivery;
using HBA.Gateway.Application.Contracts.Financial;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Driver BFF — dashboard, missions, gains (§15, §16, §23).</summary>
public sealed class DriverHandlerTests
{
    private readonly FakeDeliveryClient _delivery = new();
    private readonly FakeFinancialClient _financial = new();

    private static readonly Guid DriverId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid MissionId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private GetDriverDashboardHandler Dashboard() => new(_delivery, _financial);
    private GetDriverMissionsHandler Missions() => new(_delivery);
    private GetDriverEarningsHandler Earnings() => new(_delivery, _financial);

    private void GivenDriver(string availability = "Available")
        => _delivery.AccountResult = ServiceResult<DriverAccount>.Success(
            200, Fixtures.Driver(DriverId, availability));

    [Fact]
    public async Task Le_dashboard_resout_le_driverId_depuis_le_compte()
    {
        GivenDriver();
        _financial.WalletResult = ServiceResult<DriverWallet>.Success(
            200, new DriverWallet(DriverId, 42_500m, 1_250_000m, "XOF"));

        var response = await Dashboard().HandleAsync(CancellationToken.None);

        // LE JETON PORTE UN userId ; LE PORTEFEUILLE EXIGE UN driverId.
        _financial.LastDriverId.Should().Be(DriverId);
        response.Data.Today.AvailableBalance.Should().Be(42_500m);
    }

    [Fact]
    public async Task Le_dashboard_reste_utile_sans_les_gains()
    {
        GivenDriver();
        // financial rend 502 par défaut.

        var response = await Dashboard().HandleAsync(CancellationToken.None);

        response.Data.Driver.FullName.Should().Be("Hector Adjovi");
        response.Data.Today.AvailableBalance.Should().BeNull();

        // Dépendance IMPORTANTE : l'écran s'affiche, amputé, ET le dit.
        response.Warnings.Should().ContainSingle()
            .Which.Source.Should().Be("Financial");
    }

    /// <summary>Un compte HBA sans dossier livreur.</summary>
    [Fact]
    public async Task Rend_404_quand_le_compte_n_a_pas_de_dossier_livreur()
    {
        // La doublure rend 404 par défaut.
        var act = () => Dashboard().HandleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Rend_503_quand_delivery_est_a_terre()
    {
        // 502 : le service est injoignable, ce qui est une PANNE.
        _delivery.AccountResult = ServiceResult<DriverAccount>.Failure(502, "delivery injoignable");

        var act = () => Dashboard().HandleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    [Fact]
    public async Task Retient_la_mission_en_cours_et_ignore_les_courses_closes()
    {
        GivenDriver();
        _delivery.MissionsResult = ServiceResult<IReadOnlyList<DriverMission>>.Success(
            200,
            [
                Fixtures.Mission(Guid.NewGuid(), "Delivered"),
                Fixtures.Mission(MissionId, "InTransit"),
            ]);

        var response = await Dashboard().HandleAsync(CancellationToken.None);

        response.Data.CurrentMission!.DeliveryId.Should().Be(MissionId);
    }

    [Fact]
    public async Task N_expose_jamais_le_prix_client()
    {
        // LE TEST LE PLUS IMPORTANT DU FICHIER (§16).
        var champs = typeof(DriverMissionDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        champs.Should().NotContain("Price");
        champs.Should().NotContain(name => name.Contains("Commission", StringComparison.OrdinalIgnoreCase));
        champs.Should().NotContain(name => name.Contains("Margin", StringComparison.OrdinalIgnoreCase));
        champs.Should().Contain("EstimatedEarning");

        // Et la projection ne le recopie pas non plus, quel que soit le DTO.
        var dto = DriverProjections.ToDto(Fixtures.Mission(MissionId, "InTransit", price: 9_999m));
        dto.EstimatedEarning.Should().Be(1_500m);
    }

    [Fact]
    public async Task Le_detail_d_une_mission_d_un_autre_livreur_est_introuvable()
    {
        _delivery.MissionsResult = ServiceResult<IReadOnlyList<DriverMission>>.Success(
            200, [Fixtures.Mission(MissionId, "InTransit")]);

        // LA MISSION EST CHERCHÉE DANS **SES** MISSIONS.
        var act = () => Missions().GetAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Les_missions_sont_paginees_par_la_passerelle()
    {
        _delivery.MissionsResult = ServiceResult<IReadOnlyList<DriverMission>>.Success(
            200, [.. Enumerable.Range(0, 30).Select(_ => Fixtures.Mission(Guid.NewGuid(), "Delivered"))]);

        var response = await Missions().ListAsync(
            new PageRequest(page: 2, pageSize: 10), activeOnly: false, CancellationToken.None);

        // Le service ne pagine pas : la troncature est faite ici, après avoir reçu
        // l'historique complet.
        response.Data.Items.Should().HaveCount(10);
        response.Data.Page.Should().Be(2);
    }

    [Fact]
    public async Task Le_filtre_actif_ecarte_les_courses_closes()
    {
        _delivery.MissionsResult = ServiceResult<IReadOnlyList<DriverMission>>.Success(
            200,
            [
                Fixtures.Mission(Guid.NewGuid(), "Delivered"),
                Fixtures.Mission(MissionId, "PickedUp"),
            ]);

        var response = await Missions().ListAsync(
            new PageRequest(1, 20), activeOnly: true, CancellationToken.None);

        response.Data.Items.Should().ContainSingle()
            .Which.DeliveryId.Should().Be(MissionId);
    }

    [Fact]
    public async Task Le_solde_est_critique_sur_l_ecran_revenus()
    {
        GivenDriver();
        // financial rend 502 par défaut.

        // MÊME SERVICE, DEUX CRITICITÉS — c'est la démonstration du §23.
        var act = () => Earnings().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    [Fact]
    public async Task Les_revenus_s_affichent_sans_le_detail_des_mouvements()
    {
        GivenDriver();
        _financial.WalletResult = ServiceResult<DriverWallet>.Success(
            200, new DriverWallet(DriverId, 42_500m, 1_250_000m, "XOF"));
        _financial.TransactionsResult =
            ServiceResult<IReadOnlyList<WalletTransaction>>.Failure(502, "indisponible");

        var response = await Earnings().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        response.Data.AvailableBalance.Should().Be(42_500m);
        response.Data.Movements.Items.Should().BeEmpty();
        response.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(BffWarning.ServiceUnavailable);
    }
}
