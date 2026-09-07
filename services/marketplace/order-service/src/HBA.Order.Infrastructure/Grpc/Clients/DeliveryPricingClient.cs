using HBA.DeliveryPricing.Contracts;
using HBA.DeliveryPricing.Grpc.V1;

using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Globalization;

// COPIE DEPUIS `HBA.DeliveryPricing.Contracts.Grpc` (lot D — dissolution des
// assemblages de contrats).

namespace HBA.Orders.Infrastructure.Grpc.Clients;

internal static class DeliveryPricingGrpcRegistration
{
    public static IServiceCollection AddDeliveryPricingGrpcClient(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration["Services:DeliveryPricing"]
            ?? throw new InvalidOperationException("Services:DeliveryPricing est absent - impossible de joindre delivery-pricing-service.");
        var grpcPort = configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()?.GrpcPort ?? new HostingOptions().GrpcPort;
        var uri = new UriBuilder(address) { Port = grpcPort }.Uri;

        services.AddGrpcClient<DeliveryPricingApi.DeliveryPricingApiClient>(options => options.Address = uri)
            .AjouterLesInterceptionsInternes();

        // LA RELECTURE DE DEVIS EST ENREGISTRÉE ICI, PAS CHEZ L'APPELANT.
        services.AddScoped<IDeliveryQuoteLookup, DeliveryQuoteLookupClient>();

        return services;
    }
}

/// <summary><see cref="IDeliveryQuoteLookup"/> par-dessus le RPC de delivery-pricing.</summary>
internal sealed class DeliveryQuoteLookupClient : IDeliveryQuoteLookup
{
    private readonly DeliveryPricingApi.DeliveryPricingApiClient _client;

    public DeliveryQuoteLookupClient(DeliveryPricingApi.DeliveryPricingApiClient client)
        => _client = client;

    public async Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default)
    {
        // ON N'APPELLE PAS LE RÉSEAU POUR UN IDENTIFIANT VIDE.
        if (string.IsNullOrWhiteSpace(quoteId))
        {
            return null;
        }

        var response = await _client.LookupQuoteAsync(
            new LookupQuoteRequest { QuoteId = quoteId },
            cancellationToken: cancellationToken);

        if (!response.Found)
        {
            return null;
        }

        return new DeliveryQuoteDetails(
            response.QuoteId,

            // C'EST ICI QUE L'ARGENT CHANGE DE REPRÉSENTATION (D39).
            response.Total,

            response.Currency,
            response.EstimatedMinutes,
            response.DistanceKm,
            Horodatage(response.ExpiresAt),
            response.IsExpired,
            response.IsConsumed,
            response.PickupLatitude,
            response.PickupLongitude,
            response.DropoffLatitude,
            response.DropoffLongitude,
            response.DeliveryType,

            // Voir l'encadré de `DeliveryQuoteDetails.PartnerId` : delivery-pricing
            // n'a aucune notion de partenaire.
            PartnerId: null,

            // Recopié tel quel, sans repli sur « FALLBACK_HAVERSINE ».
            EstimationSource: response.EstimationSource);
    }

    private static DateTime Horodatage(string valeur)
        => DateTime.TryParse(
            valeur, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parse)
            ? parse.ToUniversalTime()
            : DateTime.MinValue;
}
