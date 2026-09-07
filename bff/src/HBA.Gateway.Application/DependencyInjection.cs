using HBA.Gateway.Application.Bff;
using HBA.Gateway.Application.Bff.Admin;
using HBA.Gateway.Application.Bff.Client.Express;
using HBA.Gateway.Application.Bff.Client.Food;
using HBA.Gateway.Application.Bff.Driver;
using HBA.Gateway.Application.Bff.Merchant;
using HBA.Gateway.Application.Bff.Restaurant;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Gateway.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Enregistre les agrégations BFF.
    /// </summary>
    /// <remarks>
    /// CETTE MÉTHODE NE LIT AUCUNE CONFIGURATION, ET C'EST UNE FRONTIÈRE.
    ///
    /// Lier et valider `BffAggregationOptions` exigerait `ValidateOnStart`, qui
    /// vit dans `Microsoft.Extensions.Hosting` — c'est-à-dire l'hôte générique,
    /// les services d'arrière-plan et le cycle de vie de l'application. Rien de
    /// tout cela n'a sa place dans la couche qui décrit les cas d'usage.
    ///
    /// Le binding est donc fait par le composition root (`Program.cs`), qui
    /// connaît déjà l'hôte. Application se contente de DÉCLARER la forme
    /// attendue, dans <see cref="BffAggregationOptions"/>.
    /// </remarks>
    public static IServiceCollection AddGatewayApplication(this IServiceCollection services)
    {
        // Portée requête : l'agrégateur capture ICorrelationContext, propre à la
        // requête en cours. En singleton, toutes les requêtes partageraient
        // l'identifiant de la première — invisible en développement, redoutable
        // en production.
        services.AddScoped<HomeScreenAggregator>();
        services.AddScoped<ExpressHomeService>();
        services.AddScoped<FoodHomeService>();

        // ── Handlers d'agrégation TYPÉS (§48) ────────────────────────────────
        //
        // Portée requête, comme les façades : ils capturent des clients dont
        // l'`HttpClient` est lui-même géré par requête pour suivre le DNS.
        services.AddScoped<GetExpressHomeHandler>();
        services.AddScoped<GetProductDetailHandler>();
        services.AddScoped<GetFoodHomeHandler>();
        services.AddScoped<GetRestaurantDetailHandler>();
        services.AddScoped<GetDriverDashboardHandler>();
        services.AddScoped<GetDriverMissionsHandler>();
        services.AddScoped<GetDriverEarningsHandler>();
        services.AddScoped<GetMerchantActivitiesHandler>();
        services.AddScoped<GetMerchantDashboardHandler>();
        services.AddScoped<GetRestaurantDashboardHandler>();
        services.AddScoped<GetRestaurantKitchenHandler>();
        services.AddScoped<GetAdminQueuesHandler>();

        return services;
    }
}
