using HBA.Drivers.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Drivers.Contracts.Grpc;

public static class DriversGrpcRegistration
{
    public static IServiceCollection AddDriversGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Drivers"]
            ?? throw new InvalidOperationException("Services:Drivers est absent - impossible de joindre driver-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<DriverApi.DriverApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
