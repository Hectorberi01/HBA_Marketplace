using HBA.Financial.Grpc.V1;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


// COPIE DEPUIS `HBA.Financial.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.Marketplace.ReturnRefund.Infrastructure.Grpc.Clients;

internal static class FinancialGrpcRegistration
{
    public static IServiceCollection AddFinancialGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Financial"]
            ?? throw new InvalidOperationException("Configuration Services:Financial absente.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort
            ?? new HostingOptions().GrpcPort;

        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services
            .AddGrpcClient<FinancialApi.FinancialApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        return services;
    }
}
