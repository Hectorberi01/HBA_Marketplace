using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Admin;
using HBA.Gateway.Application.Contracts.Analytics;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Admin BFF — les courbes de la plateforme.</summary>
public sealed class AdminAnalyticsHandlerTests
{
    private readonly FakeAnalyticsClient _analytics = new();

    private GetAdminAnalyticsHandler Handler() => new(_analytics);

    private static readonly DateOnly Aujourdhui = DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateOnly Debut(int jours) => Aujourdhui.AddDays(-(jours - 1));

    private static PlatformActivitySeries Activite(DateOnly du, DateOnly au)
    {
        var points = new List<PlatformActivityPoint>();
        for (var jour = du; jour <= au; jour = jour.AddDays(1))
        {
            points.Add(new PlatformActivityPoint(jour, 2, 30_000m, 1, 1));
        }

        return new PlatformActivitySeries(
            du, au, "XOF", points, points.Sum(p => p.OrdersCount), points.Sum(p => p.Gmv));
    }

    private static SignupSeries Inscriptions(DateOnly du, DateOnly au)
    {
        var points = new List<SignupPoint>();
        for (var jour = du; jour <= au; jour = jour.AddDays(1))
        {
            points.Add(new SignupPoint(jour, 5, 1));
        }

        return new SignupSeries(du, au, points, points.Sum(p => p.Buyers), points.Sum(p => p.Sellers));
    }

    private void GivenLesDeux(int jours)
    {
        var du = Debut(jours);
        _analytics.ActivityResult = ServiceResult<PlatformActivitySeries>.Success(200, Activite(du, Aujourdhui));
        _analytics.SignupsResult = ServiceResult<SignupSeries>.Success(200, Inscriptions(du, Aujourdhui));
    }

    [Fact]
    public async Task Les_deux_courbes_arrivent_dans_un_seul_appel()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.Activity.Should().HaveCount(GetAdminAnalyticsHandler.DefaultDays);
        envelope.Data.Signups.Should().HaveCount(GetAdminAnalyticsHandler.DefaultDays);
        envelope.Data.Currency.Should().Be("XOF");
        envelope.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// LES DEUX COURBES SONT BORNÉES SUR LA MÊME PÉRIODE, ET C'EST TOUTE LA
    /// RAISON D'ÊTRE DE CET ÉCRAN AGRÉGÉ.
    /// </summary>
    /// <remarks>
    /// Un client qui appellerait les deux routes séparément devrait accorder ses
    /// bornes lui-même. Deux écrans le feraient deux fois, et finiraient par ne
    /// plus le faire pareil — une courbe d'inscriptions sur trente jours à côté
    /// d'une courbe de ventes sur sept se lit comme un effondrement.
    /// </remarks>
    [Fact]
    public async Task Les_deux_courbes_couvrent_exactement_la_meme_periode()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.From.Should().Be(Debut(GetAdminAnalyticsHandler.DefaultDays));
        envelope.Data.To.Should().Be(Aujourdhui);
        envelope.Data.Activity!.First().Day.Should().Be(envelope.Data.Signups!.First().Day);
        envelope.Data.Activity!.Last().Day.Should().Be(envelope.Data.Signups!.Last().Day);
    }

    [Fact]
    public async Task La_periode_demandee_est_bornee_a_366_jours()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.MaxDays);

        var envelope = await Handler().HandleAsync(100_000, CancellationToken.None);

        envelope.Data.From.Should().Be(Debut(GetAdminAnalyticsHandler.MaxDays));
        _analytics.LastFrom.Should().Be(Debut(GetAdminAnalyticsHandler.MaxDays));
    }

    /// <summary>
    /// UNE COURBE À TERRE N'EMPORTE PAS L'AUTRE — c'est la règle de tout le
    /// contrôleur admin : un service muet ne coûte pas l'écran.
    /// </summary>
    [Fact]
    public async Task Une_courbe_indisponible_laisse_l_autre_s_afficher()
    {
        var du = Debut(GetAdminAnalyticsHandler.DefaultDays);
        _analytics.ActivityResult = ServiceResult<PlatformActivitySeries>.Failure(503, "analytics à terre");
        _analytics.SignupsResult = ServiceResult<SignupSeries>.Success(200, Inscriptions(du, Aujourdhui));

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.Activity.Should().BeNull();
        envelope.Data.TotalOrders.Should().BeNull();
        envelope.Data.TotalGmv.Should().BeNull();
        envelope.Data.Currency.Should().BeNull();
        envelope.Data.Signups.Should().NotBeNull();
        envelope.Warnings.Should().Contain(w => w.Source == "Analytics");
    }

    /// <summary>
    /// AUCUN TOTAL « COMPTES CRÉÉS » N'EST RENDU, ET CE TEST EST LÀ POUR QUE
    /// PERSONNE NE L'AJOUTE PAR COMMODITÉ.
    /// </summary>
    /// <remarks>
    /// Un vendeur s'inscrit d'abord comme utilisateur : il compte dans `Buyers`
    /// ET dans `Sellers`. Sommer les deux surcompterait exactement les vendeurs.
    /// </remarks>
    [Fact]
    public async Task Les_inscriptions_gardent_leurs_deux_series_separees()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        var jour = envelope.Data.Signups!.First();
        jour.Buyers.Should().Be(5);
        jour.Sellers.Should().Be(1);

        typeof(AdminAnalyticsDto).GetProperties().Select(p => p.Name)
            .Should().NotContain("TotalSignups");
    }
}
