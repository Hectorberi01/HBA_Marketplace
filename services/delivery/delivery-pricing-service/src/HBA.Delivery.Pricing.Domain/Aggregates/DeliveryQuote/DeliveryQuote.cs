using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;

/// <summary>
/// LES MONTANTS SONT DES `long`, ET C'EST L'UNE DES DEUX SEULES ÎLES EN ENTIER DU
/// DÉPÔT (D39).
/// </summary>
public sealed record DeliveryQuote(
    Guid Id,
    Guid? SellerId,
    Guid? StoreId,
    GeoPoint Pickup,
    GeoPoint Dropoff,
    int DistanceMeters,
    int DurationSeconds,
    string? VehicleType,
    string ServiceLevel,
    long Subtotal,
    PriceBreakdown Components,
    long Discount,
    long Total,
    string Currency,
    DateTimeOffset ExpiresAt,
    string PricingVersion,
    string Status)
{
    // LE CONSTRUCTEUR QU'EF SAIT LIER — SANS LUI, LE SERVICE NE DÉMARRE PAS.
    private DeliveryQuote(
        Guid id,
        Guid? sellerId,
        Guid? storeId,
        int distanceMeters,
        int durationSeconds,
        string? vehicleType,
        string serviceLevel,
        long subtotal,
        long discount,
        long total,
        string currency,
        DateTimeOffset expiresAt,
        string pricingVersion,
        string status)
        : this(
            id,
            sellerId,
            storeId,
            null!,
            null!,
            distanceMeters,
            durationSeconds,
            vehicleType,
            serviceLevel,
            subtotal,
            null!,
            discount,
            total,
            currency,
            expiresAt,
            pricingVersion,
            status)
    {
    }

    public Guid? ConsumedByDeliveryId { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }

    /// <summary>
    /// D'où viennent <see cref="DistanceMeters"/> et <see cref="DurationSeconds"/>
    /// : <c> CLIENT_PROVIDED</c> ou <c> FALLBACK_HAVERSINE</c>.
    /// </summary>
    public string SourceEstimation { get; init; } = string.Empty;

    /// <summary>Le facteur de correction urbaine EFFECTIVEMENT appliqué à ce devis-ci.</summary>
    public decimal FacteurCorrectionApplique { get; init; }
}
