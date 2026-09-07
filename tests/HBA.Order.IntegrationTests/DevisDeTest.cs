using HBA.DeliveryPricing.Contracts;

namespace HBA.Order.IntegrationTests;

/// <summary>LA RELECTURE DE DEVIS, EN MÉMOIRE — ET ELLE LÈVE.</summary>
internal sealed class DevisDeTest : IDeliveryQuoteLookup
{
    public Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Aucun test de cette suite ne présente de devis au checkout. "
            + "En rendre un ici court-circuiterait la garde qui empêche l'acheteur "
            + "de fixer ses propres frais de livraison.");
}
