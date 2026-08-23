using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using HBA.Tracking.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Tracking.Contracts.Grpc;

public static class TrackingGrpcRegistration
{
    public static IServiceCollection AddTrackingGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Tracking"]
            ?? throw new InvalidOperationException("Services:Tracking est absent - impossible de joindre tracking-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<TrackingApi.TrackingApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
