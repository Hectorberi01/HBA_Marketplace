using HBA.Routes.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Routes.Contracts.Grpc;

public static class RoutesGrpcRegistration
{
    public static IServiceCollection AddRoutesGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Routes"]
            ?? throw new InvalidOperationException("Services:Routes est absent - impossible de joindre route-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<RouteApi.RouteApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
