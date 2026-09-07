using System.Globalization;
using HBA.Gateway.Application.Abstractions.Services;
using HBA.Gateway.Application.Contracts.Analytics;
using HBA.Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace HBA.Gateway.Infrastructure.HttpClients.Analytics;

/// <inheritdoc cref="IAnalyticsClient" />
public sealed class AnalyticsClient : ServiceHttpClient, IAnalyticsClient
{
    /// <summary>
    /// Le format de date attendu par le service.
    /// </summary>
    /// <remarks>
    /// `InvariantCulture` EXPLICITE, MÊME AVEC `InvariantGlobalization`.
    ///
    /// Le dépôt pose `InvariantGlobalization=true`, donc la culture courante EST
    /// l'invariante aujourd'hui. S'appuyer là-dessus ferait dépendre le format
    /// d'une ligne de `Directory.Build.props` que personne ne relierait jamais à
    /// une date mal comprise en face : un `yyyy-MM-dd` rendu autrement produirait
    /// un 400, ou pire, une période décalée.
    /// </remarks>
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
}
