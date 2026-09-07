using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Catalog;
using HBA.Gateway.Application.Contracts.Engagement;
using HBA.Gateway.Application.Contracts.Order;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Accueil HBAExpress (§6, §22, §23, §45).</summary>
public sealed class ExpressHomeHandlerTests
{
    private readonly FakeCatalogClient _catalog = new();
    private readonly FakeEngagementClient _engagement = new();
    private readonly FakeOrderClient _order = new();

    private GetExpressHomeHandler Handler() => new(_catalog, _engagement, _order);

    private void GivenCategories(params CatalogCategory[] categories)
        => _catalog.CategoriesResult =
            ServiceResult<IReadOnlyList<CatalogCategory>>.Success(200, categories);

    [Fact]
    public async Task Ecarte_les_categories_non_publiees()
    {
        GivenCategories(
            Fixtures.Category("Telephones"),
            Fixtures.Category("Brouillon", status: "Draft"));

        var response = await Handler().HandleAsync(CancellationToken.None);

        // catalog-service rend TOUT : le filtrage d'une vitrine publique est ici.
        response.Data.Categories.Should().ContainSingle()
            .Which.Name.Should().Be("Telephones");
    }

    [Fact]
    public async Task Rend_503_quand_le_catalogue_est_a_terre()
    {
        _catalog.CategoriesResult =
            ServiceResult<IReadOnlyList<CatalogCategory>>.Failure(502, "catalog injoignable");

        var act = () => Handler().HandleAsync(CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    [Fact]
    public async Task S_affiche_pour_un_visiteur_anonyme()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        // Engagement et Order rendent 401 par défaut : aucune session.
        var response = await Handler().HandleAsync(CancellationToken.None);

        response.Data.Categories.Should().HaveCount(1);
        response.Data.RecommendedProducts.Should().BeEmpty();
        response.Data.ActiveOrder.Should().BeNull();

        // Deux 401 attendus : l'écran est complet du point de vue du visiteur.
        response.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task Hydrate_les_recommandations_et_plafonne_les_appels()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        var ids = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            _catalog.ProductsById[id] = Fixtures.Product(id, $"P{id:N}");
        }

        _engagement.RecommendationsResult = ServiceResult<RecommendationSet>.Success(
            200,
            new RecommendationSet(
                Guid.NewGuid(), "personal", null, Guid.NewGuid(), ids, 0.9, DateTime.UtcNow));

        var response = await Handler().HandleAsync(CancellationToken.None);

        // TRENTE RECOMMANDATIONS NE DOIVENT PAS FAIRE TRENTE APPELS.
        //
        // Sans plafond, l'accueil paierait un aller-retour par recommandation —
        // le N+1 du §43, sur l'écran le plus consulté de l'application.
        _catalog.ProductCallCount.Should().Be(GetExpressHomeHandler.RecommendationCardCount);
        response.Data.RecommendedProducts.Should().HaveCount(
            GetExpressHomeHandler.RecommendationCardCount);
    }

    [Fact]
    public async Task Omet_silencieusement_un_produit_recommande_disparu()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        var vivant = Guid.NewGuid();
        var supprime = Guid.NewGuid();
        _catalog.ProductsById[vivant] = Fixtures.Product(vivant, "Vivant");

        _engagement.RecommendationsResult = ServiceResult<RecommendationSet>.Success(
            200,
            new RecommendationSet(
                Guid.NewGuid(), "personal", null, null, [vivant, supprime], 0.5, DateTime.UtcNow));

        var response = await Handler().HandleAsync(CancellationToken.None);

        // Les recommandations sont calculées en différé : elles citent parfois un
        // produit retiré depuis. L'accueil ne doit pas en dépendre.
        response.Data.RecommendedProducts.Should().ContainSingle()
            .Which.Name.Should().Be("Vivant");
        response.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Retient_la_commande_en_cours_la_plus_recente()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        var ancienne = new OrderBrief(Guid.NewGuid(), "Paid", "XOF", 10_000m, DateTime.UtcNow.AddDays(-2));
        var recente = new OrderBrief(Guid.NewGuid(), "Shipped", "XOF", 25_000m, DateTime.UtcNow);
        var livree = new OrderBrief(Guid.NewGuid(), "Delivered", "XOF", 5_000m, DateTime.UtcNow.AddHours(-1));

        _order.Result = ServiceResult<IReadOnlyList<OrderBrief>>.Success(200, [ancienne, livree, recente]);

        var response = await Handler().HandleAsync(CancellationToken.None);

        // « Delivered » n'est pas un statut actif : le bandeau ne doit pas
        // ressusciter une commande close, fût-elle la plus récente.
        response.Data.ActiveOrder!.Id.Should().Be(recente.Id);
    }

    [Fact]
    public async Task N_expose_aucune_donnee_de_restauration()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        var response = await Handler().HandleAsync(CancellationToken.None);

        // §6 et §45 : la séparation des deux univers doit être portée par le TYPE.
        // Si quelqu'un ajoute un champ « restaurants » à `ExpressHomeDto`, ce test
        // ne suffira pas — mais la revue de ce nom, si.
        typeof(ExpressHomeDto).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(name =>
                name.Contains("Restaurant", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Food", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Dish", StringComparison.OrdinalIgnoreCase));

        response.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task Lance_les_trois_dependances_en_parallele()
    {
        GivenCategories(Fixtures.Category("Telephones"));

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            _catalog.ProductsById[id] = Fixtures.Product(id);
        }

        _engagement.RecommendationsResult = ServiceResult<RecommendationSet>.Success(
            200, new RecommendationSet(Guid.NewGuid(), "p", null, null, ids, 1, DateTime.UtcNow));

        var gate = new TaskCompletionSource();
        _catalog.Gate = gate;

        var pending = Handler().HandleAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref _catalog.ProductCallCount) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        _catalog.ProductCallCount.Should().Be(2, "les deux hydratations partent ensemble");

        gate.SetResult();
        await pending;
    }
}
