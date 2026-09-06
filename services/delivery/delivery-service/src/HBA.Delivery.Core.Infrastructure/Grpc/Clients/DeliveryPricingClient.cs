using HBA.DeliveryPricing.Contracts;
using HBA.DeliveryPricing.Grpc.V1;

using HBA.Shared.Hosting.Grpc;
using HBA.Shared.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using System.Globalization;

// ═════════════════════════════════════════════════════════════════════════════
// COPIE DEPUIS `HBA.DeliveryPricing.Contracts.Grpc` (lot D — dissolution des assemblages de contrats).
//
// `shared/` ne contient plus que les `.proto`. Ce service compile lui-meme le
// contrat dont il a besoin, et porte donc sa propre traduction.
//
// LES TYPES GENERES SONT `internal` A CET ASSEMBLAGE. Deux services qui
// compilent le meme proto obtiennent deux types CLR distincts ; les rendre
// publics ferait, dans un hote compose, deux types publics du meme nom complet —
// CS0433, a l'usage, loin de la cause. Les adaptateurs et mappings sont donc
// `internal` eux aussi : un type public dont la signature expose un type interne
// ne compile pas.
//
// CE QUE ÇA COUTE : cette traduction existe en 4 exemplaires dans le depot,
// un par service qui appelle ce domaine. Elles sont identiques aujourd'hui et
// rien n'empeche qu'elles divergent. C'est le prix de l'autonomie par service,
// paye ici en connaissance de cause.
// ═════════════════════════════════════════════════════════════════════════════

namespace HBA.Deliveries.Infrastructure.Grpc.Clients;

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
        //
        // Elle vient avec le client : un service qui sait joindre delivery-pricing
        // sait relire un devis. L'enregistrer service par service aurait donné
        // deux endroits à tenir d'accord — et order-service comme
        // food-order-service en dépendent tous les deux pour leur checkout.
        services.AddScoped<IDeliveryQuoteLookup, DeliveryQuoteLookupClient>();

        return services;
    }
}

/// <summary>
/// <see cref="IDeliveryQuoteLookup"/> par-dessus le RPC de delivery-pricing.
/// </summary>
internal sealed class DeliveryQuoteLookupClient : IDeliveryQuoteLookup
{
    private readonly DeliveryPricingApi.DeliveryPricingApiClient _client;

    public DeliveryQuoteLookupClient(DeliveryPricingApi.DeliveryPricingApiClient client)
        => _client = client;

    public async Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default)
    {
        // ON N'APPELLE PAS LE RÉSEAU POUR UN IDENTIFIANT VIDE.
        //
        // Le serveur refuserait en `InvalidArgument`, ce qui est correct de sa
        // part — mais l'appelant, lui, doit lire « pas de devis », pas une
        // exception. C'est le cas d'une commande passée SANS devis, qui est
        // ordinaire côté marchandise.
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
            //
            // `Total` est un `int64` : delivery-pricing compte en FRANCS ENTIERS,
            // le franc CFA n'ayant pas de sous-unité. La conversion implicite vers
            // `decimal` est EXACTE — 1500 devient 1500,00 — parce que les deux
            // côtés comptent la même unité.
            //
            // NE JAMAIS ÉCRIRE `/ 100` NI `* 100` SUR CETTE LIGNE. Aucune
            // conversion de ce genre n'existe dans le dépôt ; en ajouter une
            // reviendrait à supposer des centimes, et diviserait par cent les
            // frais de port de chaque commande.
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
            // n'a aucune notion de partenaire. Nul, toujours, et sciemment.
            PartnerId: null,

            // Recopié tel quel, sans repli sur « FALLBACK_HAVERSINE ». Un serveur
            // d'une version antérieure renvoie une chaîne vide, et vide se lit
            // « on ne sait pas ». Deviner ici transformerait une absence
            // d'information en affirmation.
            EstimationSource: response.EstimationSource);
    }

    /// <remarks>
    /// UN REPLI SUR `MinValue`, ET IL EST SÛR — CONTRAIREMENT AUX ZÉROS
    /// SILENCIEUX DE D39.
    ///
    /// Un montant illisible rendu à zéro RELÂCHE un contrôle ; une date illisible
    /// rendue à `MinValue` en RESSERRE un — le devis paraît expiré depuis
    /// toujours. Et surtout, cette date ne DÉCIDE rien : `IsExpired` est tranché
    /// par le serveur et voyage à part. Elle ne sert qu'au message affiché au
    /// client. Le repli dégrade donc une phrase, jamais une règle.
    ///
    /// `RoundtripKind` ET `InvariantCulture`. Le serveur écrit en « O », donc
    /// avec son décalage. Sans `RoundtripKind`, .NET rendrait un `DateTime` en
    /// heure LOCALE du conteneur qui lit — et l'échéance d'un devis se déplacerait
    /// d'une heure selon le fuseau de la machine.
    /// </remarks>
    private static DateTime Horodatage(string valeur)
        => DateTime.TryParse(
            valeur, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parse)
            ? parse.ToUniversalTime()
            : DateTime.MinValue;
}
