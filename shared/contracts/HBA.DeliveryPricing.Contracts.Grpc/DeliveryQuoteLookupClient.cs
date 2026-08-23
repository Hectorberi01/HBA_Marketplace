using System.Globalization;
using HBA.DeliveryPricing.Grpc.V1;

namespace HBA.DeliveryPricing.Contracts.Grpc;

/// <summary>
/// <see cref="IDeliveryQuoteLookup"/> par-dessus le RPC de delivery-pricing.
/// </summary>
public sealed class DeliveryQuoteLookupClient : IDeliveryQuoteLookup
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
            PartnerId: null);
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
