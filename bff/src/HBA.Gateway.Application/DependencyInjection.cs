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
    /// <summary>Enregistre les agrégations BFF.</summary>
    public static IServiceCollection AddGatewayApplication(this IServiceCollection services)
    {
        // Portée requête : l'agrégateur capture ICorrelationContext, propre à la
        // requête en cours.
        services.AddScoped<HomeScreenAggregator>();
        services.AddScoped<ExpressHomeService>();
        services.AddScoped<FoodHomeService>();

        // ── Handlers d'agrégation TYPÉS (§48) ────────────────────────────────
        services.AddScoped<GetExpressHomeHandler>();
        services.AddScoped<GetProductDetailHandler>();
        services.AddScoped<GetFoodHomeHandler>();
        services.AddScoped<GetRestaurantDetailHandler>();
        services.AddScoped<GetDriverDashboardHandler>();
        services.AddScoped<GetDriverMissionsHandler>();
        services.AddScoped<GetDriverEarningsHandler>();
        services.AddScoped<GetMerchantActivitiesHandler>();
        services.AddScoped<GetMerchantDashboardHandler>();
        services.AddScoped<GetMerchantAnalyticsHandler>();
        services.AddScoped<GetRestaurantDashboardHandler>();
        services.AddScoped<GetRestaurantKitchenHandler>();
        services.AddScoped<GetAdminQueuesHandler>();
        services.AddScoped<GetAdminAnalyticsHandler>();

        return services;
    }
}
