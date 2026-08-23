using HBA.Financial.Grpc.V1;
using HBA.Shared.Hosting;
using HBA.Shared.Hosting.Grpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HBA.Financial.Contracts.Grpc;

public static class FinancialGrpcRegistration
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
