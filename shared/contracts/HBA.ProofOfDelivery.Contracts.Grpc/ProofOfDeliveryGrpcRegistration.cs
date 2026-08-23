using HBA.ProofOfDelivery.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.ProofOfDelivery.Contracts.Grpc;

public static class ProofOfDeliveryGrpcRegistration
{
    public static IServiceCollection AddProofOfDeliveryGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:ProofOfDelivery"]
            ?? throw new InvalidOperationException("Services:ProofOfDelivery est absent - impossible de joindre proof-of-delivery-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<ProofApi.ProofApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
