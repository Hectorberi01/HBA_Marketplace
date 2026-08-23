using HBA.Dispatch.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Dispatch.Contracts.Grpc;

public static class DispatchGrpcRegistration
{
    public static IServiceCollection AddDispatchGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Dispatch"]
            ?? throw new InvalidOperationException("Services:Dispatch est absent - impossible de joindre dispatch-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<DispatchApi.DispatchApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
