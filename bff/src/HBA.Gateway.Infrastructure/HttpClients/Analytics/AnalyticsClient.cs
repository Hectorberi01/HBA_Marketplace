using System.Globalization;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Analytics;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Analytics;

/// <inheritdoc cref="IAnalyticsClient" />
public sealed class AnalyticsClient : ServiceHttpClient, IAnalyticsClient
{
    /// <summary>Le format de date attendu par le service.</summary>
    private static string Borne(DateOnly jour) => jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public AnalyticsClient(HttpClient http, ILogger<AnalyticsClient> logger) : base(http, logger)
    {
    }

    public override string ServiceKey => ServiceKeys.Analytics;

    public Task<ServiceResult<SellerSalesSeries>> GetSellerSalesAsync(
        Guid sellerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => GetAsync<SellerSalesSeries>(
            $"/api/sellers/{sellerId}/analytics/sales?from={Borne(from)}&to={Borne(to)}",
            cancellationToken);

    public Task<ServiceResult<PlatformActivitySeries>> GetPlatformActivityAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => GetAsync<PlatformActivitySeries>(
            $"/api/admin/analytics/activity?from={Borne(from)}&to={Borne(to)}",
            cancellationToken);

    public Task<ServiceResult<SignupSeries>> GetSignupsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => GetAsync<SignupSeries>(
            $"/api/admin/analytics/signups?from={Borne(from)}&to={Borne(to)}",
            cancellationToken);

    // Pas de `currency` transmis : le service normalise vers sa devise par
    // defaut, la meme que pour l'activite. Les deux montants de l'ecran restent
    // donc dans une seule devise.
    public Task<ServiceResult<PaymentSeries>> GetPaymentsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => GetAsync<PaymentSeries>(
            $"/api/admin/analytics/payments?from={Borne(from)}&to={Borne(to)}",
            cancellationToken);
}
