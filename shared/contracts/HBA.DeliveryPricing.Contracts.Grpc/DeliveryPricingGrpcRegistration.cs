using HBA.DeliveryPricing.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.DeliveryPricing.Contracts.Grpc;

public static class DeliveryPricingGrpcRegistration
{
    public static IServiceCollection AddDeliveryPricingGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:DeliveryPricing"]
            ?? throw new InvalidOperationException("Services:DeliveryPricing est absent - impossible de joindre delivery-pricing-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<DeliveryPricingApi.DeliveryPricingApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        // LA RELECTURE DE DEVIS EST ENREGISTRÉE ICI, PAS CHEZ L'APPELANT.
        //
        // Elle vient avec le client : un service qui sait joindre delivery-pricing
        // sait relire un devis. L'enregistrer service par service aurait donné
        // deux endroits à tenir d'accord — et order-service comme
        // food-order-service en dépendent tous les deux pour leur checkout.
        services.AddScoped<IDeliveryQuoteLookup, DeliveryQuoteLookupClient>();

        return services;
    }
}
