using FluentAssertions;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.Bff.Shared;
using HBA.Gateway.Application.Contracts.Food;
using HBA.Gateway.Application.Contracts.Order;
using Xunit;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>Client BFF Food — vitrine et fiche (§8, §9, §19, §45).</summary>
public sealed class FoodHandlerTests
{
    private readonly FakeFoodClient _food = new();
    private readonly FakeOrderClient _order = new();

    private static readonly Guid RestaurantId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private GetFoodHomeHandler Home() => new(_food, _order);
    private GetRestaurantDetailHandler Detail() => new(_food);

    [Fact]
    public async Task Vitrine_borne_la_taille_de_page_demandee()
    {
        await Home().HandleAsync(new PageRequest(page: 1, pageSize: 100_000), CancellationToken.None);

        // LA BORNE EST APPLIQUÉE AVANT L'APPEL SORTANT.
        //
        // S'en remettre au service amont supposerait que les treize services
        // bornent tous, de la même façon, pour toujours.
        _food.LastPageSize.Should().Be(PageRequest.MaxPageSize);
    }

    [Fact]
    public async Task Rend_503_quand_food_est_a_terre()
    {
        _food.StorefrontResult =
            ServiceResult<IReadOnlyList<RestaurantCard>>.Failure(502, "food injoignable");

        var act = () => Home().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        await act.Should().ThrowAsync<CriticalDependencyException>();
    }

    [Fact]
    public async Task Vitrine_s_affiche_pour_un_visiteur_anonyme()
    {
        _food.StorefrontResult = ServiceResult<IReadOnlyList<RestaurantCard>>.Success(
            200, [Fixtures.Card("Chez Mama"), Fixtures.Card("Le Berlin", open: false)]);

        // order rend 401 par défaut : aucune session.
        var response = await Home().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        response.Data.Restaurants.Items.Should().HaveCount(2);
        response.Data.ActiveOrder.Should().BeNull();

        // Un service qui refuse légitimement n'est pas un service en panne.
        response.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task Un_restaurant_ferme_reste_dans_la_vitrine()
    {
        _food.StorefrontResult = ServiceResult<IReadOnlyList<RestaurantCard>>.Success(
            200, [Fixtures.Card("Le Berlin", open: false)]);

        var response = await Home().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        // « visible » N'EST PAS « accepte des commandes ».
        //
        // Filtrer sur les horaires viderait l'application chaque nuit. Le client
        // consulte la carte et reviendra demain.
        response.Data.Restaurants.Items.Should().ContainSingle()
            .Which.IsOpenNow.Should().BeFalse();
    }

    [Fact]
    public async Task N_expose_aucun_produit_marketplace()
    {
        var champs = typeof(FoodHomeDto).GetProperties().Select(p => p.Name).ToList();

        // §8 et §45 : la séparation des deux univers est portée par le TYPE.
        champs.Should().NotContain(name =>
            name.Contains("Product", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Express", StringComparison.OrdinalIgnoreCase));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Fiche_ecarte_les_cartes_inactives_mais_garde_celles_hors_creneau()
    {
        _food.DetailResult = ServiceResult<RestaurantDetail>.Success(
            200, Fixtures.Detail(RestaurantId, "Chez Mama"));
        _food.MenuResult = ServiceResult<RestaurantMenu>.Success(200, Fixtures.Menu(RestaurantId));

        var response = await Detail().HandleAsync(RestaurantId, CancellationToken.None);

        // La fabrique déclare deux cartes : une active, une archivée.
        response.Data.Menus.Should().ContainSingle()
            .Which.Name.Should().Be("Carte du soir");
    }

    [Fact]
    public async Task Fiche_ne_promet_aucun_tarif_de_livraison()
    {
        _food.DetailResult = ServiceResult<RestaurantDetail>.Success(
            200, Fixtures.Detail(RestaurantId, "Chez Mama"));
        _food.MenuResult = ServiceResult<RestaurantMenu>.Success(200, Fixtures.Menu(RestaurantId));

        var response = await Detail().HandleAsync(RestaurantId, CancellationToken.None);

        // §9 ET §19 : « Ne retourne pas de faux tarif Delivery ».
        //
        // Un montant affiché est lu comme un engagement, et l'écart se découvre
        // au paiement.
        response.Data.Delivery.Available.Should().BeFalse();
        response.Data.Delivery.Fee.Should().BeNull();
        response.Data.Delivery.EtaMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Fiche_reste_lisible_sans_la_carte()
    {
        _food.DetailResult = ServiceResult<RestaurantDetail>.Success(
            200, Fixtures.Detail(RestaurantId, "Chez Mama"));
        // MenuResult non renseigné : la doublure rend 502.

        var response = await Detail().HandleAsync(RestaurantId, CancellationToken.None);

        response.Data.Restaurant.Name.Should().Be("Chez Mama");
        response.Data.Menus.Should().BeEmpty();

        // Dépendance IMPORTANTE : l'écran s'affiche, amputé, ET le dit.
        response.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(BffWarning.ServiceUnavailable);
    }

    [Fact]
    public async Task Fiche_rend_404_pour_un_etablissement_hors_vitrine()
    {
        // Le service rend 404 sans distinguer « inexistant » de « suspendu ».
        var act = () => Detail().HandleAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<BffResourceNotFoundException>();
    }

    [Fact]
    public async Task Les_deux_accueils_partagent_la_meme_notion_de_commande_active()
    {
        // CE TEST EXISTE POUR EMPÊCHER LA DIVERGENCE.
        //
        // La liste des statuts actifs vivait en dur dans l'accueil Express. La
        // recopier côté Food aurait garanti qu'un statut ajouté d'un seul côté
        // fasse disparaître le bandeau dans l'autre — sans que personne le
        // signale, parce que cela ressemble à « je n'ai pas de commande ».
        FoodOrderStatuses.IsActive("Shipped").Should().BeTrue();
        FoodOrderStatuses.IsActive("Delivered").Should().BeFalse();

        _food.StorefrontResult = ServiceResult<IReadOnlyList<RestaurantCard>>.Success(200, []);
        _order.Result = ServiceResult<IReadOnlyList<OrderBrief>>.Success(
            200,
            [
                new OrderBrief(Guid.NewGuid(), "Delivered", "XOF", 5_000m, DateTime.UtcNow),
                new OrderBrief(Guid.NewGuid(), "Preparing", "XOF", 12_500m, DateTime.UtcNow.AddMinutes(-5)),
            ]);

        var response = await Home().HandleAsync(new PageRequest(1, 20), CancellationToken.None);

        response.Data.ActiveOrder!.Status.Should().Be("Preparing");
    }
}
