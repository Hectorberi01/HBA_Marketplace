using Grpc.Core;
using HBA.Deliveries.Application.Abstractions;
using HBA.DeliveryPricing.Grpc.V1;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Infrastructure.Pricing;

public sealed class GrpcDeliveryPricingQuoteValidator : IDeliveryPricingQuoteValidator
{
    private readonly DeliveryPricingApi.DeliveryPricingApiClient _client;

    public GrpcDeliveryPricingQuoteValidator(DeliveryPricingApi.DeliveryPricingApiClient client)
    {
        _client = client;
    }

    public async Task<Result<DeliveryPricingQuoteValidation>> ConsumeQuoteAsync(
        string quoteId,
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(quoteId, out var parsedQuoteId))
        {
            return Result.Failure<DeliveryPricingQuoteValidation>(
                Error.Validation("pricing.quote.malformed", "Référence de devis illisible."));
        }

        try
        {
            var response = await _client.ConsumeQuoteAsync(
                new ConsumeQuoteRequest
                {
                    QuoteId = parsedQuoteId.ToString(),
                    DeliveryId = deliveryId.ToString()
                },
                cancellationToken: cancellationToken);

            // ═════════════════════════════════════════════════════════════════
            // ICI L'ARGENT CHANGE DE REPRÉSENTATION (D39).
            //
            // `response.Total` est un `int64` : delivery-pricing compte en FRANCS
            // ENTIERS, comme promotions, parce que le franc CFA n'a pas de
            // sous-unité. `DeliveryPricingQuoteValidation.Total` est un `decimal`,
            // comme tout le reste du dépôt.
            //
            // La conversion implicite `long → decimal` est EXACTE : 1 500 devient
            // 1 500,00. Elle est correcte parce que les deux côtés comptent la
            // même unité.
            //
            // NE JAMAIS ÉCRIRE `/ 100` NI `* 100` SUR CETTE LIGNE. Il n'existe
            // aucune conversion de ce genre dans tout le dépôt — vérifié au lot
            // 8.9. En ajouter une reviendrait à supposer des centimes, et
            // multiplierait ou diviserait par cent le prix de chaque course.
            // ═════════════════════════════════════════════════════════════════
            return new DeliveryPricingQuoteValidation(
                parsedQuoteId,
                response.Valid,
                response.Status,
                response.HasTotal ? response.Total : null,
                response.HasCurrency ? response.Currency : null);
        }
        // ═════════════════════════════════════════════════════════════════════
        // QUATRE STATUTS, ET NON PLUS DEUX — CE FILTRE ÉTAIT LE SEUL DU DÉPÔT
        // ET IL LAISSAIT PASSER LES DEUX PANNES LES PLUS PROBABLES.
        //
        // `Unauthenticated` : clé interne absente ou fausse chez l'appelé. Elle
        // remontait auparavant en `NotFound` — donc hors de ce filtre — et
        // traversait `CreateDeliveryCommand` en exception brute. Un incident
        // d'authentification déguisé en défaut de domaine.
        //
        // `FailedPrecondition` : `Internal:ApiKey` non configurée chez l'appelé.
        // Elle arrivait en `Unavailable`, donc rattrapée par chance, pour la
        // mauvaise raison.
        //
        // Dans les QUATRE cas, ce que le domaine doit savoir est le même : le
        // devis n'a pas pu être consommé parce que le service tarifaire n'a pas
        // répondu. Le CODE d'erreur, lui, reste distinct dans les journaux — c'est
        // là qu'on cherche la cause, pas dans le type du Result.
        //
        // CE QUI N'EST PAS RATTRAPÉ RESTE DÉLIBÉRÉMENT NON RATTRAPÉ.
        // `InvalidArgument` (identifiant malformé) et `Internal` (bug de l'appelé)
        // doivent remonter : les traduire en « service indisponible » ferait
        // chercher une panne réseau devant un bug de code. C'est exactement le
        // défaut relevé sur `OrderGrpcClient`.
        // ═════════════════════════════════════════════════════════════════════
        catch (RpcException exception) when (exception.StatusCode
            is StatusCode.Unavailable
            or StatusCode.DeadlineExceeded
            or StatusCode.Unauthenticated
            or StatusCode.FailedPrecondition)
        {
            return Result.Failure<DeliveryPricingQuoteValidation>(
                Error.DependencyUnavailable(
                    $"pricing.grpc_{exception.StatusCode.ToString().ToLowerInvariant()}",
                    "Delivery Pricing Service est indisponible."));
        }
    }
}
