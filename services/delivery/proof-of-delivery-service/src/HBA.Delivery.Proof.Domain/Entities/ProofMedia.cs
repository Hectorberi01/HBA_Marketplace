namespace HBA.Delivery.Proof.Domain.Entities;

public sealed record ProofMedia(
    Guid Id,
    Guid ProofId,
    string MediaType,
    string ObjectKey,
    string MimeType,
    string Sha256,
    DateTimeOffset CapturedAt,
    long SizeBytes);
