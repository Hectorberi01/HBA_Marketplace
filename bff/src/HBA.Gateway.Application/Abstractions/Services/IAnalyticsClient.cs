using HBA.Gateway.Application.Contracts.Analytics;

namespace HBA.Gateway.Application.Abstractions.Services;

/// <summary>Client sortant vers <c>analytics-service</c>.</summary>
public interface IAnalyticsClient : IServiceClient
{
    /// <summary>
    /// <c> GET /api/sellers/{sellerId}/analytics/sales</c> — AUTHENTIFIÉ, et gardé
    /// en face par l'appartenance vendeur plus <c> SELLER_ANALYTICS_VIEW</c>.
    /// </summary>
    Task<ServiceResult<SellerSalesSeries>> GetSellerSalesAsync(
        Guid sellerId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary><c>GET /api/admin/analytics/activity</c> — RÔLE ADMIN exigé en face.</summary>
    Task<ServiceResult<PlatformActivitySeries>> GetPlatformActivityAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary><c>GET /api/admin/analytics/signups</c> — RÔLE ADMIN exigé en face.</summary>
    Task<ServiceResult<SignupSeries>> GetSignupsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary><c>GET /api/admin/analytics/payments</c> — RÔLE ADMIN exigé en face.</summary>
    Task<ServiceResult<PaymentSeries>> GetPaymentsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
