using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Engagement;
using HBA.Gateway.Application.Contracts.Merchant;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Fiche produit HBAExpress — le gabarit de dégradation (§23, §41).</summary>
public sealed class ProductDetailHandlerTests
{
    private readonly FakeCatalogClient _catalog = new();
    private readonly FakeInventoryClient _inventory = new();
    private readonly FakeEngagementClient _engagement = new();
    private readonly FakeMerchantClient _merchant = new();

    private static readonly Guid ProductId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid VariantA = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid VariantB = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private GetProductDetailHandler Handler()
        => new(_catalog, _inventory, _engagement, _merchant);

    private void GivenProduct()
        => _catalog.ProductsById[ProductId] =
            Fixtures.Product(ProductId, "Écouteurs", (VariantA, "SKU-A"), (VariantB, "SKU-B"));

    [Fact]
    public async Task Rend_la_fiche_complete_quand_tout_repond()
    {
        GivenProduct();
        _inventory.StockBySku["SKU-A"] = 12;
        _inventory.StockBySku["SKU-B"] = 0;
        _engagement.RatingResult = ServiceResult<ProductRating>.Success(
            200, new ProductRating(ProductId, 4.6, 128));
        _merchant.Result = ServiceResult<StoreShowcase>.Success(
            200, new StoreShowcase(Guid.NewGuid(), "HBA Tech Store", null, null, "+229", true));

        var response = await Handler().HandleAsync(ProductId, CancellationToken.None);

        response.Warnings.Should().BeEmpty();
        response.Data.Product.Name.Should().Be("Écouteurs");
        response.Data.Rating!.Count.Should().Be(128);
        response.Data.Store!.Name.Should().Be("HBA Tech Store");

        // Le stock est associé PAR SKU, pas par position : une inversion des
        // réponses parallèles afficherait le stock de A sur B.
        response.Data.Variants.Single(v => v.Sku == "SKU-A").Available.Should().Be(12);
        response.Data.Variants.Single(v => v.Sku == "SKU-B").Available.Should().Be(0);
    }

    [Fact]
    public async Task Place_le_media_principal_en_tete()
    {
        GivenProduct();

        var response = await Handler().HandleAsync(ProductId, CancellationToken.None);

        // La fabrique déclare le média secondaire AVANT le principal : sans tri,
        // l'application afficherait la mauvaise vignette.
        response.Data.Media.First().IsPrimary.Should().BeTrue();
        response.Data.Media.First().Url.Should().Be("https://cdn/1.jpg");
    }

    [Fact]
    public async Task Rend_404_quand_le_produit_n_existe_pas()
    {
        // Catalogue joignable, produit absent : ce n'est PAS une panne.
        var act = () => Handler().HandleAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Rend_503_quand_le_catalogue_est_a_terre()
    {
        _catalog.ProductResult = ServiceResult<Application.Contracts.Catalog.CatalogProduct>
            .Failure(502, "catalog injoignable");

        var act = () => Handler().HandleAsync(ProductId, CancellationToken.None);

        // Dépendance CRITIQUE : aucune fiche partielle n'a de sens sans produit.
        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    [Fact]
    public async Task Stock_indisponible_donne_null_et_non_zero()
    {
        GivenProduct();
        _inventory.Down = true;

        var response = await Handler().HandleAsync(ProductId, CancellationToken.None);

        // LE TEST LE PLUS IMPORTANT DU FICHIER.
        //
        // `Available = 0` ferait afficher « rupture » sur tout le catalogue
        // pendant une panne d'inventaire, et perdre les ventes correspondantes.
        response.Data.Variants.Should().OnlyContain(variant => variant.Available == null);

        // `ContainSingle` ÉTAIT FAUX, ET LE CODE AVAIT RAISON.
        //
        // La fiche produit émet TOUJOURS un second avertissement : merchant-service
        // n'expose aucune vitrine publique de boutique, donc `NOT_CONFIGURED` est
        // permanent tant que la route n'existe pas.
        //
        // Mon assertion supposait un écran sans autre dégradation — une hypothèse
        // que le fichier de test lui-même contredit deux tests plus bas. Vérifier
        // la PRÉSENCE de l'avertissement d'inventaire est ce qui était voulu ;
        // verrouiller leur NOMBRE ne prouvait rien et cassait au premier manque
        // supplémentaire.
        response.Warnings.Should().Contain(
            new BffWarning("Inventory", BffWarning.ServiceUnavailable));
    }

    [Fact]
    public async Task Boutique_absente_est_signalee_comme_non_configuree()
    {
        GivenProduct();

        // La doublure rend 501 par défaut, comme l'implémentation réelle tant
        // qu'aucune route publique de boutique n'existe.
        var response = await Handler().HandleAsync(ProductId, CancellationToken.None);

        response.Data.Store.Should().BeNull();
        response.Warnings.Should().Contain(
            warning => warning.Source == "Merchant" && warning.Code == BffWarning.NotConfigured);
    }

    [Fact]
    public async Task Note_absente_hors_session_n_emet_aucun_avertissement()
    {
        GivenProduct();
        _inventory.StockBySku["SKU-A"] = 1;
        _inventory.StockBySku["SKU-B"] = 1;
        _merchant.Result = ServiceResult<StoreShowcase>.Success(
            200, new StoreShowcase(Guid.NewGuid(), "Boutique", null, null, "+229", true));

        // engagement rend 401 par défaut : visiteur anonyme.
        var response = await Handler().HandleAsync(ProductId, CancellationToken.None);

        response.Data.Rating.Should().BeNull();

        // Un service qui refuse LÉGITIMEMENT n'est pas un service en panne.
        // Émettre un avertissement ici remplirait le tableau à chaque visite
        // non connectée, et les vrais n'y seraient plus lus.
        response.Warnings.Should().NotContain(warning => warning.Source == "Engagement");
    }

    [Fact]
    public async Task Appelle_les_dependances_en_parallele()
    {
        GivenProduct();
        _inventory.StockBySku["SKU-A"] = 1;
        _inventory.StockBySku["SKU-B"] = 1;

        var gate = new TaskCompletionSource();
        _inventory.Gate = gate;

        var pending = Handler().HandleAsync(ProductId, CancellationToken.None);

        // Les deux appels de stock doivent être PARTIS avant qu'aucun ne soit
        // résolu. En séquentiel, le compteur serait bloqué à 1.
        await WaitUntilAsync(() => Volatile.Read(ref _inventory.CallCount) == 2);

        gate.SetResult();
        var response = await pending;

        _inventory.CallCount.Should().Be(2);
        response.Data.Variants.Should().HaveCount(2);
    }

    [Fact]
    public async Task Propage_l_annulation()
    {
        GivenProduct();
        using var cts = new CancellationTokenSource();
        _inventory.Gate = new TaskCompletionSource();

        var pending = Handler().HandleAsync(ProductId, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    /// <summary>
    /// Attend une condition sans <c>Task.Delay</c> fixe.
    /// </summary>
    /// <remarks>
    /// Un délai fixe rend le test soit lent, soit instable sur une machine
    /// d'intégration chargée. Le plafond n'existe que pour ne pas bloquer la suite
    /// si la condition ne survient jamais.
    /// </remarks>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }

        condition().Should().BeTrue("la condition attendue ne s'est pas produite dans le délai");
    }
}
