namespace HBA.Deliveries.Contracts;

/// <summary>Un arrêt de course, décrit par le donneur d'ordre.</summary>
public sealed record DeliveryStopRequest(
    string? ContactName,
    string? Phone,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Instructions = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>Le colis, tel que l'appelant le décrit.</summary>
public sealed record DeliveryPackageRequest(
    string? Description,
    decimal? WeightKg = null,
    bool IsFragile = false,
    bool IsPerishable = false);

/// <summary>Demande de création d'une course.</summary>
/// <param name="Reference">
/// La référence du DONNEUR D'ORDRE — « ORDER-&lt;guid&gt; », « FOOD-&lt;guid&gt; ».
/// </param>
/// <param name="Source">« HbaExpress », « HbaFood » ou « ExternalPartner ».</param>
/// <param name="Type">« Express », « Standard » ou « Scheduled ».</param>
public sealed record CreateDeliveryRequest(
    string Reference,
    string Source,
    string Type,
    DeliveryStopRequest Pickup,
    DeliveryStopRequest Dropoff,
    DeliveryPackageRequest Package,

    // « RequiredProof » A DISPARU DE CE CONTRAT — ISSUE-057.
    decimal? DeclaredValue = null,
    bool IsCashOnDelivery = false,
    Guid? PartnerId = null,
    string? QuoteId = null,
    DateTime? ScheduledForUtc = null);

/// <summary>Résultat d'une création.</summary>
/// <param name="ReasonCode">
/// LE CODE NORMALISÉ, ET IL EST EN QUEUE AVEC UNE VALEUR PAR DÉFAUT.
/// </param>
public sealed record DeliveryCreationResult(
    bool Succeeded, Guid DeliveryId, string? Reason, string? ReasonCode = null);

/// <summary>Résultat d'une annulation demandée par le donneur d'ordre.</summary>
public sealed record DeliveryCancellationResult(
    bool Found, bool Cancelled, string? Reason, string? ReasonCode = null);

// `DeliveryQuoteDetails` A ÉTÉ DÉPLACÉ dans `HBA.DeliveryPricing.Contracts`, avec
// la relecture qu'il décrivait.

/// <summary>La surface d'ÉCRITURE du moteur logistique, ouverte aux donneurs d'ordre.</summary>
public interface IDeliveryDispatchApi
{
    // `RequestQuoteAsync` ET `LookupQuoteAsync` ONT QUITTÉ CE CONTRAT.

    Task<DeliveryCreationResult> CreateAsync(
        CreateDeliveryRequest request, CancellationToken cancellationToken = default);

    /// <summary>LE DONNEUR D'ORDRE DÉFAIT CE QU'IL A DEMANDÉ.</summary>
    Task<DeliveryCancellationResult> CancelByReferenceAsync(
        string reference, string source, string? reason, CancellationToken cancellationToken = default);
}
