using Google.Protobuf.WellKnownTypes;
using HBA.FoodCarts.Infrastructure.Grpc.Mappers;
using HBA.Promotion.Grpc.V1;

using HBA.Promotions.Contracts;
using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


// COPIE DEPUIS `HBA.Promotions.Contracts.Grpc` (lot D — dissolution des assemblages
// de contrats).

namespace HBA.FoodCarts.Infrastructure.Grpc.Clients;

/// <summary>Côté CLIENT : implémente <see cref="IPromotionModuleApi"/> par gRPC.</summary>
internal sealed class PromotionGrpcClient : IPromotionModuleApi
{
    private readonly PromotionApi.PromotionApiClient _client;

    public PromotionGrpcClient(PromotionApi.PromotionApiClient client) => _client = client;

    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string code, PromotionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.EvaluatePromotionAsync(
            new EvaluatePromotionRequest { Code = code ?? string.Empty, Context = context.ToProto() },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<CouponReservationResult?> ReserveAsync(
        string code, Guid userId, Guid cartId, PromotionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReserveCouponAsync(
            new ReserveCouponRequest
            {
                Code = code ?? string.Empty,
                UserId = userId.ToString(),
                CartId = cartId.ToString(),
                Context = context.ToProto()
            },
            cancellationToken: cancellationToken);

        return reponse.ToContract();
    }

    public async Task<bool> CommitAsync(
        Guid reservationId, Guid orderId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.CommitCouponAsync(
            new CommitCouponRequest
            {
                ReservationId = reservationId.ToString(),
                OrderId = orderId.ToString()
            },
            cancellationToken: cancellationToken);

        return reponse.Committed;
    }

    public async Task<bool> ReleaseAsync(
        Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reponse = await _client.ReleaseCouponAsync(
            new ReleaseCouponRequest { ReservationId = reservationId.ToString() },
            cancellationToken: cancellationToken);

        return reponse.Released;
    }
}

internal static class PromotionGrpcRegistration
{
    /// <summary>Branche <see cref="IPromotionModuleApi"/> sur promotion-service, en gRPC.</summary>
    public static IServiceCollection AddPromotionGrpcClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:Promotion"]
            ?? throw new InvalidOperationException(
                "Services:Promotion est absent — impossible de joindre promotion-service.");

        var grpcPort = configuration.GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;

        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services
            .AddGrpcClient<PromotionApi.PromotionApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        services.AddScoped<IPromotionModuleApi, PromotionGrpcClient>();

        return services;
    }
}
