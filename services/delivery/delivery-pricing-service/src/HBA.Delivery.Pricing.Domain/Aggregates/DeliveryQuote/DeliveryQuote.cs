using HBA.Delivery.Pricing.Domain.ValueObjects;

namespace HBA.Delivery.Pricing.Domain.Aggregates.DeliveryQuote;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES MONTANTS SONT DES `long`, ET C'EST L'UNE DES DEUX SEULES ÎLES EN ENTIER
/// DU DÉPÔT (D39). L'autre est `promotions`.
///
/// Le franc CFA n'a pas de sous-unité : l'entier ferme la porte aux arrondis au
/// lieu de les gérer. Partout ailleurs — commandes, paiements, portefeuille,
/// courses — l'argent est en `numeric(18,2)`.
///
/// LA FRONTIÈRE EST FRANCHIE UNE FOIS, ET DANS UN SEUL SENS.
///
/// `ConsumeQuote` rend `int64 total` ; `GrpcDeliveryPricingQuoteValidator` le
/// reçoit en `decimal?`. La conversion est implicite et EXACTE — 1 500 devient
/// 1 500,00 — parce que les deux côtés comptent en FRANCS. Il n'y a nulle part,
/// dans tout le dépôt, de multiplication ni de division par cent : le jour où
/// quelqu'un en écrit une ici, c'est qu'il a supposé des centimes.
///
/// CE QUE CE CHOIX SUPPOSE : que la devise n'a pas de sous-unité. Rien ne le
/// vérifie — `Currency` est un texte libre de trois lettres.
/// ═════════════════════════════════════════════════════════════════════════════
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
    public Guid? ConsumedByDeliveryId { get; init; }
    public DateTimeOffset? ConsumedAt { get; init; }
}
