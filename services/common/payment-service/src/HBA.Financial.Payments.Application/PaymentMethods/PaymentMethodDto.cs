namespace HBA.Financial.Payments.Application.PaymentMethods;

/// <summary>Projection client d'un moyen de paiement enregistré.</summary>
public sealed record PaymentMethodDto(
    Guid Id,
    string Type,
    string Label,
    string Provider,
    string Display,
    int? ExpiryMonth,
    int? ExpiryYear,
    string? HolderName,
    bool IsDefault);
