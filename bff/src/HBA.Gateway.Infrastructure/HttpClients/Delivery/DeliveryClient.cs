using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Delivery;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Delivery;

/// <inheritdoc cref="IDeliveryClient" />
public sealed class DeliveryClient : ServiceHttpClient, IDeliveryClient
{
    public DeliveryClient(HttpClient http, ILogger<DeliveryClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Delivery;

    // AUCUN IDENTIFIANT DANS L'URL : le service résout depuis le jeton, que
    // `OutboundHeaderPropagationHandler` transmet. Passer un `driverId` ici
    // rouvrirait la faille que `ResolveDriverQuery` a fermée côté service.
    public Task<ServiceResult<DriverAccount>> GetMyDriverAccountAsync(
        CancellationToken cancellationToken)
        => GetAsync<DriverAccount>("/api/deliveries/drivers/me", cancellationToken);

    public Task<ServiceResult<IReadOnlyList<DriverMission>>> ListMyMissionsAsync(
        CancellationToken cancellationToken)
        => GetAsync<IReadOnlyList<DriverMission>>(
            "/api/deliveries/drivers/me/missions", cancellationToken);
}
