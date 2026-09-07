using System.Text.Json;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Catalog;
using HBA.Gateway.Application.Contracts.Engagement;
using HBA.Gateway.Application.Contracts.Delivery;
using HBA.Gateway.Application.Contracts.Financial;
using HBA.Gateway.Application.Contracts.Food;
using HBA.Gateway.Application.Contracts.Inventory;
using HBA.Gateway.Application.Contracts.Merchant;
using HBA.Gateway.Application.Contracts.Order;

namespace HBA.Gateway.IntegrationTests.Bff;

/// <summary>
/// Doublures des clients Application, écrites à la main.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// PAS DE BIBLIOTHÈQUE DE SIMULACRES, ET C'EST UN CHOIX.
///
/// Aucune n'est déclarée dans `Directory.Packages.props` ; en ajouter une
/// imposerait une version de plus à toute la solution pour six interfaces.
///
/// L'écrire à la main a un second mérite : ces doublures COMPTENT les appels et
/// exposent une porte (`Gate`) qui permet de prouver la parallélisation du §22 —
/// ce qu'un simulacre configuré par expression ne montre pas.
///
/// CES DOUBLURES NE REMPLACENT PAS LES TESTS DE CONTRAT (§42).
///
/// Elles vérifient la LOGIQUE d'agrégation. Elles ne peuvent rien dire de la
/// conformité des `Contracts/` aux réponses réelles des services : c'est
/// précisément le risque du miroir recopié, et il se couvre en intégration.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class FakeCatalogClient : ICatalogClient
{
    public string ServiceKey => "Catalog";

    public ServiceResult<CatalogProduct>? ProductResult { get; set; }
    public ServiceResult<IReadOnlyList<CatalogCategory>>? CategoriesResult { get; set; }
    public Dictionary<Guid, CatalogProduct> ProductsById { get; } = [];
    public int ProductCallCount;

    /// <summary>Retient chaque appel jusqu'à ouverture — sert aux tests de parallélisme.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<ServiceResult<CatalogProduct>> GetProductAsync(Guid id, CancellationToken ct)
    {
        Interlocked.Increment(ref ProductCallCount);

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();

        if (ProductsById.TryGetValue(id, out var known))
        {
            return ServiceResult<CatalogProduct>.Success(200, known);
        }

        return ProductResult ?? ServiceResult<CatalogProduct>.Failure(404, "absent");
    }

    public Task<ServiceResult<IReadOnlyList<CatalogCategory>>> ListCategoriesAsync(CancellationToken ct)
        => Task.FromResult(CategoriesResult
            ?? ServiceResult<IReadOnlyList<CatalogCategory>>.Success(200, []));

    public Task<ServiceResult<IReadOnlyList<CatalogProduct>>> ListSellerProductsAsync(Guid sellerId, CancellationToken ct)
        => Task.FromResult(ServiceResult<IReadOnlyList<CatalogProduct>>.Success(200, []));

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeInventoryClient : IInventoryClient
{
    public string ServiceKey => "Inventory";

    public Dictionary<string, int> StockBySku { get; } = [];
    public bool Down { get; set; }
    public int CallCount;
    public TaskCompletionSource? Gate { get; set; }

    public async Task<ServiceResult<StockAvailability>> GetAvailabilityAsync(string sku, CancellationToken ct)
    {
        Interlocked.Increment(ref CallCount);

        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();

        if (Down)
        {
            return ServiceResult<StockAvailability>.Failure(502, "inventory injoignable");
        }

        return StockBySku.TryGetValue(sku, out var quantity)
            ? ServiceResult<StockAvailability>.Success(200, new StockAvailability(sku, quantity))
            : ServiceResult<StockAvailability>.Failure(404, "sku inconnu");
    }

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeEngagementClient : IEngagementClient
{
    public string ServiceKey => "Engagement";

    public ServiceResult<ProductRating>? RatingResult { get; set; }
    public ServiceResult<RecommendationSet>? RecommendationsResult { get; set; }

    public Task<ServiceResult<ProductRating>> GetProductRatingAsync(Guid productId, CancellationToken ct)
        => Task.FromResult(RatingResult ?? ServiceResult<ProductRating>.Failure(401, "anonyme"));

    public Task<ServiceResult<IReadOnlyList<ProductReview>>> ListProductReviewsAsync(Guid productId, CancellationToken ct)
        => Task.FromResult(ServiceResult<IReadOnlyList<ProductReview>>.Success(200, []));

    public Task<ServiceResult<RecommendationSet>> GetMyRecommendationsAsync(CancellationToken ct)
        => Task.FromResult(RecommendationsResult ?? ServiceResult<RecommendationSet>.Failure(401, "anonyme"));

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeMerchantClient : IMerchantClient
{
    public string ServiceKey => "Merchant";

    public ServiceResult<StoreShowcase>? Result { get; set; }

    public ServiceResult<SellerAccount>? SellerResult { get; set; }
    public ServiceResult<IReadOnlyList<MerchantStore>>? StoresResult { get; set; }
    public ServiceResult<MerchantStore>? StoreResult { get; set; }
    public Guid LastSellerId;

    public Task<ServiceResult<StoreShowcase>> GetStoreShowcaseAsync(Guid storeId, CancellationToken ct)
        => Task.FromResult(Result
            ?? ServiceResult<StoreShowcase>.Failure(501, "aucune vitrine publique"));

    public Task<ServiceResult<SellerAccount>> GetMySellerAsync(CancellationToken ct)
        => Task.FromResult(SellerResult ?? ServiceResult<SellerAccount>.Failure(404, "pas de dossier"));

    public Task<ServiceResult<IReadOnlyList<MerchantStore>>> ListStoresAsync(Guid sellerId, CancellationToken ct)
    {
        LastSellerId = sellerId;
        return Task.FromResult(StoresResult
            ?? ServiceResult<IReadOnlyList<MerchantStore>>.Success(200, []));
    }

    public Task<ServiceResult<MerchantStore>> GetStoreAsync(Guid sellerId, Guid storeId, CancellationToken ct)
    {
        LastSellerId = sellerId;
        return Task.FromResult(StoreResult ?? ServiceResult<MerchantStore>.Failure(404, "absente"));
    }

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeOrderClient : IOrderClient
{
    public string ServiceKey => "Order";

    public ServiceResult<IReadOnlyList<OrderBrief>>? Result { get; set; }

    public ServiceResult<IReadOnlyList<OrderBrief>>? SellerOrdersResult { get; set; }

    public Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListMineAsync(CancellationToken ct)
        => Task.FromResult(Result ?? ServiceResult<IReadOnlyList<OrderBrief>>.Failure(401, "anonyme"));

    public Task<ServiceResult<IReadOnlyList<OrderBrief>>> ListBySellerAsync(Guid sellerId, CancellationToken ct)
        => Task.FromResult(SellerOrdersResult
            ?? ServiceResult<IReadOnlyList<OrderBrief>>.Success(200, []));

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeFoodClient : IFoodClient
{
    public string ServiceKey => "Food";

    public ServiceResult<IReadOnlyList<RestaurantCard>>? StorefrontResult { get; set; }
    public ServiceResult<RestaurantDetail>? DetailResult { get; set; }
    public ServiceResult<RestaurantMenu>? MenuResult { get; set; }
    public int LastPage;
    public int LastPageSize;

    public Task<ServiceResult<IReadOnlyList<RestaurantCard>>> ListStorefrontAsync(
        int page, int pageSize, CancellationToken ct)
    {
        LastPage = page;
        LastPageSize = pageSize;
        return Task.FromResult(StorefrontResult
            ?? ServiceResult<IReadOnlyList<RestaurantCard>>.Success(200, []));
    }

    public Task<ServiceResult<RestaurantDetail>> GetRestaurantAsync(Guid id, CancellationToken ct)
        => Task.FromResult(DetailResult ?? ServiceResult<RestaurantDetail>.Failure(404, "absent"));

    public ServiceResult<PartnerRestaurant>? MyRestaurantResult { get; set; }
    public ServiceResult<KitchenBoard>? KitchenResult { get; set; }

    public Task<ServiceResult<RestaurantMenu>> GetMenuAsync(Guid id, CancellationToken ct)
        => Task.FromResult(MenuResult ?? ServiceResult<RestaurantMenu>.Failure(502, "food injoignable"));

    public Task<ServiceResult<PartnerRestaurant>> GetMyRestaurantAsync(CancellationToken ct)
        => Task.FromResult(MyRestaurantResult
            ?? ServiceResult<PartnerRestaurant>.Failure(404, "aucun établissement"));

    public Task<ServiceResult<KitchenBoard>> GetKitchenAsync(Guid id, CancellationToken ct)
        => Task.FromResult(KitchenResult ?? ServiceResult<KitchenBoard>.Failure(502, "food injoignable"));

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeDeliveryClient : IDeliveryClient
{
    public string ServiceKey => "Delivery";

    public ServiceResult<DriverAccount>? AccountResult { get; set; }
    public ServiceResult<IReadOnlyList<DriverMission>>? MissionsResult { get; set; }

    public Task<ServiceResult<DriverAccount>> GetMyDriverAccountAsync(CancellationToken ct)
        => Task.FromResult(AccountResult ?? ServiceResult<DriverAccount>.Failure(404, "pas de dossier"));

    public Task<ServiceResult<IReadOnlyList<DriverMission>>> ListMyMissionsAsync(CancellationToken ct)
        => Task.FromResult(MissionsResult
            ?? ServiceResult<IReadOnlyList<DriverMission>>.Success(200, []));

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

public sealed class FakeFinancialClient : IFinancialClient
{
    public string ServiceKey => "Financial";

    public ServiceResult<DriverWallet>? WalletResult { get; set; }
    public ServiceResult<IReadOnlyList<WalletTransaction>>? TransactionsResult { get; set; }
    public Guid LastDriverId;

    public Task<ServiceResult<DriverWallet>> GetDriverWalletAsync(Guid driverId, CancellationToken ct)
    {
        LastDriverId = driverId;
        return Task.FromResult(WalletResult ?? ServiceResult<DriverWallet>.Failure(502, "financial injoignable"));
    }

    public ServiceResult<SellerWallet>? SellerWalletResult { get; set; }
    public int SellerWalletCalls;

    public Task<ServiceResult<IReadOnlyList<WalletTransaction>>> ListDriverTransactionsAsync(
        Guid driverId, int take, CancellationToken ct)
        => Task.FromResult(TransactionsResult
            ?? ServiceResult<IReadOnlyList<WalletTransaction>>.Success(200, []));

    public Task<ServiceResult<SellerWallet>> GetSellerWalletAsync(Guid sellerId, CancellationToken ct)
    {
        Interlocked.Increment(ref SellerWalletCalls);
        return Task.FromResult(SellerWalletResult
            ?? ServiceResult<SellerWallet>.Failure(502, "financial injoignable"));
    }

    public Task<ServiceResult> GetJsonAsync(string relativePath, CancellationToken ct)
        => Task.FromResult(ServiceResult.Failure(501, "non utilisé"));
}

/// <summary>Fabriques d'objets amont, pour ne pas répéter dix arguments par test.</summary>
public static class Fixtures
{
    public static CatalogProduct Product(
        Guid id, string name = "Produit", params (Guid Id, string Sku)[] variants)
        => new(
            id,
            SellerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CategoryId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            BrandId: null,
            Name: name,
            Description: "Description",
            Slug: "produit",
            Status: "Published",
            Variants: [.. variants.Select(v => new CatalogProductVariant(
                v.Id, v.Sku, new Dictionary<string, string>(), 500))],
            Media:
            [
                new CatalogProductMedia(Guid.NewGuid(), Guid.NewGuid(), "https://cdn/2.jpg", "image", false, 2, "b"),
                new CatalogProductMedia(Guid.NewGuid(), Guid.NewGuid(), "https://cdn/1.jpg", "image", true, 1, "a"),
            ]);

    public static CatalogCategory Category(string name, string status = "Published")
        => new(Guid.NewGuid(), null, name, name.ToLowerInvariant(), $"/{name}", status, null);

    public static RestaurantCard Card(string name, bool open = true)
        => new(Guid.NewGuid(), name, null, null, null, open,
            open ? "None" : "OutsideServiceHours", 20, null, "Normal", 0, null);

    public static RestaurantDetail Detail(Guid id, string name, bool accepts = true)
        => new(id, name, "Cuisine béninoise", null, null, null, "+229 97 00 00 00",
            "Active", accepts, accepts ? "None" : "NothingAvailable",
            25, "Manual", null, "Normal", 0, null,
            [new RestaurantServiceHours("Monday", "10:00", "22:00")], true);

    public static RestaurantMenu Menu(Guid restaurantId, bool sectionActive = true)
        => new(restaurantId, "Chez Mama", true, "None", 25,
            [
                new FoodMenu(Guid.NewGuid(), "Carte du soir", null, true, true, "18:00", "23:00",
                    [
                        new FoodMenuSection(Guid.NewGuid(), "Plats", null, sectionActive,
                            [
                                new FoodMenuItem(Guid.NewGuid(), "Poulet braisé", null, null, null,
                                    5500m, "XOF", true, null),
                            ]),
                    ]),
                new FoodMenu(Guid.NewGuid(), "Carte archivée", null, false, false, null, null, []),
            ]);

    public static DriverAccount Driver(Guid driverId, string availability = "Available")
        => new(driverId, Guid.NewGuid(), "Hector Adjovi", "+229 97 44 12 08", "Motorcycle",
            "Active", availability, null, 1284, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(-1));

    public static DriverStop Stop(string name)
        => new(name, "+229 97 00 00 00", "Cotonou", "Fidjrossè", "Station Oryx", null, 6.35, 2.38);

    public static DriverMission Mission(
        Guid id, string status, decimal price = 2500m, decimal earning = 1500m)
        => new(id, $"DEL-{id:N}"[..12], status, "Food",
            Stop("Chez Mama"), Stop("Sandrine A."),
            "2 sacs", 1.2m, false, "Otp", price, earning, "XOF",
            null, DateTime.UtcNow.AddMinutes(-5), null);

    public static SellerAccount Seller(Guid id)
        => new(id, Guid.NewGuid(), "HBA Tech Store", null, "Active", "Approved", 0.0914m, 4.8m, 1284);

    public static MerchantStore Store(Guid id, Guid sellerId, string name, bool selling = true)
        => new(id, sellerId, name, null, null, "+229 97 00 00 00", "Open", selling, null);

    public static PartnerRestaurant Partner(
        Guid restaurantId, string role = "Owner", Guid? payoutSellerId = null,
        params string[] permissions)
        => new(restaurantId, "Chez Mama", "Active", role, role == "Owner", true,
            permissions, payoutSellerId, true, "None");

    public static KitchenBoard Kitchen(Guid restaurantId, params (string Status, int MinutesAgo)[] tickets)
        => new(restaurantId, null, [],
            [
                .. tickets.Select(t => new KitchenTicket(
                    Guid.NewGuid(), Guid.NewGuid(), t.Status, 0, 15,
                    DateTime.UtcNow.AddMinutes(-t.MinutesAgo), null, null, null,
                    null, 0,
                    [new KitchenTicketItem(Guid.NewGuid(), "Poulet braisé", 1, null, "Pending", null, 15, [])])),
            ]);

    public static JsonElement EmptyJson => JsonDocument.Parse("{}").RootElement.Clone();
}
