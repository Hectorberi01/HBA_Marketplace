namespace HBA.DeliveryPricing.Contracts;

/// <summary>Un devis de course DÉJÀ ÉTABLI, relu par son identifiant.</summary>
/// <param name="Total">EN `decimal`, ALORS QUE delivery-pricing COMPTE EN ENTIERS (D39).</param>
/// <param name="IsExpired">
/// CALCULÉ PAR LE SERVEUR, PAS DÉDUIT DE <paramref name="ExpiresAtUtc"/> .
/// </param>
/// <param name="PartnerId">
/// TOUJOURS NUL AUJOURD'HUI, ET C'EST UN MANQUE CONNU, PAS UN OUBLI.
/// </param>
public sealed record DeliveryQuoteDetails(
    string QuoteId,
    decimal Total,
    string Currency,

    // UNE ESTIMATION BASSE QUAND `EstimationSource` VAUT « FALLBACK_HAVERSINE ».
    int EstimatedMinutes,

    double DistanceKm,
    DateTime ExpiresAtUtc,

    // Deux états distincts, jamais fondus en « invalide » : un devis expiré se
    // redemande, un devis consommé signale un rejeu ou un défaut d'intégration.
    bool IsExpired,
    bool IsConsumed,
    double PickupLatitude,
    double PickupLongitude,
    double DropoffLatitude,
    double DropoffLongitude,

    // « EXPRESS », « STANDARD », « SCHEDULED » — la chaîne du contrat, pas une
    // énumération : les contrats ne connaissent pas les énumérations du domaine.
    string DeliveryType,

    Guid? PartnerId,

    // D'OÙ VIENNENT `EstimatedMinutes` ET `DistanceKm`.
    string EstimationSource = "");

/// <summary>Relire un devis de course, pour opposer son montant à l'acheteur.</summary>
public interface IDeliveryQuoteLookup
{
    /// <summary>Le devis, ou <c>null</c> s'il n'existe pas.</summary>
    Task<DeliveryQuoteDetails?> LookupQuoteAsync(
        string? quoteId, CancellationToken cancellationToken = default);
}
