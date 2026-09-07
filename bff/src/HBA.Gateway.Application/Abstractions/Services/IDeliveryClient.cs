using HBA.Gateway.Application.Contracts.Delivery;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>delivery-service</c>.</summary>
public interface IDeliveryClient : IServiceClient
{
    /// <summary>
    /// <c> GET /api/deliveries/drivers/me</c> — AUTHENTIFIÉ, résout le compte
    /// livreur depuis le jeton.
    /// </summary>
    Task<ServiceResult<DriverAccount>> GetMyDriverAccountAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/deliveries/drivers/me/missions</c> — AUTHENTIFIÉ.</summary>
    Task<ServiceResult<IReadOnlyList<DriverMission>>> ListMyMissionsAsync(
        CancellationToken cancellationToken);
}
