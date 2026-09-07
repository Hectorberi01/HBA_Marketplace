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
            points.Add(new SignupPoint(jour, 5, 1, 2));
        }

        return new SignupSeries(
            du, au, points,
            points.Sum(p => p.Buyers), points.Sum(p => p.Sellers), points.Sum(p => p.Drivers));
    }

    private static PaymentSeries Paiements(DateOnly du, DateOnly au)
    {
        var points = new List<PaymentPoint>();
        for (var jour = du; jour <= au; jour = jour.AddDays(1))
        {
            points.Add(new PaymentPoint(jour, 3, 1, 45_000m));
        }

        // LE PLUS GROS VOLUME N'EST PAS LE MEILLEUR TAUX, et c'est voulu :
        // « kkiapay » encaisse moins mais echoue moins. Un test qui les
        // rangerait dans le meme ordre ne distinguerait pas les deux criteres.
        var prestataires = new List<PaymentProvider>
        {
            new("fedapay", 40, 20, 600_000m, 0.3333m),
            new("kkiapay", 20, 1, 300_000m, 0.0476m),
            new("inconnu", 5, 0, 75_000m, 0m),
        };

        return new PaymentSeries(
            du, au, "XOF", points, prestataires,
            points.Sum(p => p.Captured), points.Sum(p => p.Failed), 0.25m);
    }

    private void GivenLesDeux(int jours)
    {
        var du = Debut(jours);
        _analytics.ActivityResult = ServiceResult<PlatformActivitySeries>.Success(200, Activite(du, Aujourdhui));
        _analytics.SignupsResult = ServiceResult<SignupSeries>.Success(200, Inscriptions(du, Aujourdhui));
    }

    private void GivenLesTrois(int jours)
    {
        GivenLesDeux(jours);
        _analytics.PaymentsResult = ServiceResult<PaymentSeries>.Success(200, Paiements(Debut(jours), Aujourdhui));
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
    /// LES DEUX COURBES SONT BORNÉES SUR LA MÊME PÉRIODE, ET C'EST TOUTE LA RAISON
    /// D'ÊTRE DE CET ÉCRAN AGRÉGÉ.
    /// </summary>
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
    [Fact]
    public async Task Les_inscriptions_gardent_leurs_deux_series_separees()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        var jour = envelope.Data.Signups!.First();
        jour.Buyers.Should().Be(5);
        jour.Sellers.Should().Be(1);
        jour.Drivers.Should().Be(2);

        typeof(AdminAnalyticsDto).GetProperties().Select(p => p.Name)
            .Should().NotContain("TotalSignups");
    }

    [Fact]
    public async Task Les_trois_courbes_arrivent_dans_un_seul_appel()
    {
        GivenLesTrois(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.Activity.Should().HaveCount(GetAdminAnalyticsHandler.DefaultDays);
        envelope.Data.Signups.Should().HaveCount(GetAdminAnalyticsHandler.DefaultDays);
        envelope.Data.Payments.Should().HaveCount(GetAdminAnalyticsHandler.DefaultDays);
        envelope.Data.Payments!.First().Day.Should().Be(envelope.Data.Activity!.First().Day);
        envelope.Data.Payments!.Last().Day.Should().Be(envelope.Data.Activity!.Last().Day);
        envelope.Warnings.Should().BeEmpty();
    }

    /// <summary>
    /// LE CLASSEMENT DES PRESTATAIRES EST CELUI DU SERVICE, DU PLUS GROS VOLUME
    /// AU PLUS PETIT — et surtout PAS par taux d'échec.
    /// </summary>
    [Fact]
    public async Task Le_classement_des_prestataires_n_est_pas_reordonne()
    {
        GivenLesTrois(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.PaymentProviders!.Select(p => p.Provider)
            .Should().Equal("fedapay", "kkiapay", "inconnu");

        // Le premier du classement a le PIRE taux des deux vrais prestataires :
        // trier par taux inverserait l'ordre, et ce test tomberait.
        envelope.Data.PaymentProviders![0].FailureRate
            .Should().BeGreaterThan(envelope.Data.PaymentProviders![1].FailureRate!.Value);
    }

    /// <summary>
    /// « inconnu » EST RENDU TEL QUEL, ET NE DOIT PAS ÊTRE FILTRÉ. C'est le seau
    /// des messages d'avant le lot 2 : il décroît jusqu'à zéro dans les jours qui
    /// suivent un déploiement, et sa disparition du graphe est l'information.
    /// </summary>
    [Fact]
    public async Task Le_prestataire_inconnu_reste_visible()
    {
        GivenLesTrois(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.PaymentProviders.Should().Contain(p => p.Provider == "inconnu");
    }

    /// <summary>
    /// LES PAIEMENTS TOMBENT SEULS : les deux autres courbes restent.
    /// </summary>
    [Fact]
    public async Task Les_paiements_absents_ne_font_pas_tomber_les_autres_courbes()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.Payments.Should().BeNull();
        envelope.Data.PaymentProviders.Should().BeNull();
        envelope.Data.TotalCaptured.Should().BeNull();
        envelope.Data.PaymentFailureRate.Should().BeNull();
        envelope.Data.Activity.Should().NotBeNull();
        envelope.Data.Signups.Should().NotBeNull();
        envelope.Data.Currency.Should().Be("XOF");
        envelope.Warnings.Should().Contain(w => w.Source == "Analytics");
    }

    /// <summary>
    /// LA DEVISE SURVIT A LA PERTE DE L'ACTIVITE. Elle vient alors des paiements,
    /// plutôt que d'être `null` parce que l'AUTRE série est tombée.
    /// </summary>
    [Fact]
    public async Task La_devise_vient_des_paiements_quand_l_activite_manque()
    {
        var du = Debut(GetAdminAnalyticsHandler.DefaultDays);
        _analytics.SignupsResult = ServiceResult<SignupSeries>.Success(200, Inscriptions(du, Aujourdhui));
        _analytics.PaymentsResult = ServiceResult<PaymentSeries>.Success(200, Paiements(du, Aujourdhui));
        _analytics.ActivityResult = ServiceResult<PlatformActivitySeries>.Failure(503, "activité à terre");

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.Currency.Should().Be("XOF");
        envelope.Data.TotalGmv.Should().BeNull();
        envelope.Data.TotalCaptured.Should().Be(3 * GetAdminAnalyticsHandler.DefaultDays);
    }

    /// <summary>
    /// AUCUN TOTAL NE MELANGE `Gmv` ET `CapturedAmount`. Le premier est un volume
    /// marchand — ni livraison ni commission, zéro pour un repas ; le second est
    /// ce que l'acheteur a réellement payé. Les sommer ne veut rien dire.
    /// </summary>
    [Fact]
    public async Task Aucun_total_ne_melange_le_volume_marchand_et_l_encaisse()
    {
        typeof(AdminAnalyticsDto).GetProperties().Select(p => p.Name)
            .Should().NotContain(["TotalAmount", "TotalRevenue", "GrandTotal"]);

        GivenLesTrois(GetAdminAnalyticsHandler.DefaultDays);
        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        envelope.Data.TotalGmv.Should().NotBe(envelope.Data.Payments!.Sum(p => p.CapturedAmount));
    }

    /// <summary>
    /// LES LIVREURS SONT UNE TROISIÈME SÉRIE, PAS UNE PART DES DEUX AUTRES.
    /// </summary>
    [Fact]
    public async Task Les_livreurs_arrivent_dans_leur_propre_serie()
    {
        GivenLesDeux(GetAdminAnalyticsHandler.DefaultDays);

        var envelope = await Handler().HandleAsync(null, CancellationToken.None);

        var jour = envelope.Data.Signups!.First();
        jour.Drivers.Should().Be(2);
        (jour.Buyers + jour.Sellers).Should().NotBe(jour.Buyers + jour.Sellers + jour.Drivers);
    }
}
