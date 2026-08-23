namespace HBA.Drivers.Application.Accounts;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE DOSSIER, TEL QUE SON TITULAIRE ET L'EXPLOITATION LE VOIENT.
///
/// AUCUN DE CES TROIS OBJETS NE PORTE DE POSITION NI DE DISPONIBILITÉ.
///
/// La tentation est réelle — l'écran livreur affiche les deux. Mais ce service ne
/// les connaît pas : elles vivent dans `deliveries.drivers` et se lisent par les
/// routes livreur de delivery-service. Les recopier ici obligerait à les tenir à
/// jour, donc à les écrire, donc à avoir deux écrivains sur un fait dont le
/// dispatch dépend. Voir aussi l'encadré « NE PAS Y AJOUTER LA POSITION » de
/// `DriverAccountView` chez delivery-service, qui refuse la même chose.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
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
///
/// CE QUE CETTE RÉPONSE COUVRE, ET CE QU'ELLE NE COUVRE PAS.
///
/// Elle répond sur le DOSSIER : vérifié, non suspendu, véhicule adapté. Elle ne
/// dit RIEN de la disponibilité du moment ni de la position — ce service ne les
/// détient pas. Un appelant qui prendrait `Eligible = true` pour « proposez-lui la
/// course » proposerait des courses à des livreurs hors ligne. Le seul appelant
/// légitime est celui qui veut savoir si une personne A LE DROIT de livrer.
/// </summary>
public sealed record DriverEligibilityDto(Guid DriverId, bool Eligible, string? Reason);
