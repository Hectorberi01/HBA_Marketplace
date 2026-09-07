namespace HBA.Drivers.Application.Accounts;

/// <summary>LE DOSSIER, TEL QUE SON TITULAIRE ET L'EXPLOITATION LE VOIENT.</summary>
public sealed record DriverAccountDto(
    Guid DriverId,
    Guid UserId,
    string FullName,
    string Phone,
    string VerificationStatus,
    string? StatusReason,
    bool Dispatchable,
    DateTime RegisteredAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? DecidedAtUtc,
    IReadOnlyList<DriverDocumentDto> Documents,
    IReadOnlyList<DriverVehicleDto> Vehicles,
    IReadOnlyList<string> MissingDocuments);

public sealed record DriverDocumentDto(
    Guid Id,
    string Type,
    string Status,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? RejectionReason);

public sealed record DriverVehicleDto(
    Guid Id,
    string Type,
    string? Make,
    string? Model,
    string? Plate,
    bool Active,
    decimal? CapacityKg);

/// <summary>
/// Réponse à « ce livreur peut-il prendre cette course ? », posée par le port
/// interne et par gRPC.
/// </summary>
public sealed record DriverEligibilityDto(Guid DriverId, bool Eligible, string? Reason);
