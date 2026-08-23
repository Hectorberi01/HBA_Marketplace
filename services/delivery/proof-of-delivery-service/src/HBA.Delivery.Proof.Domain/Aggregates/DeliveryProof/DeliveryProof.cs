namespace HBA.ProofOfDelivery.Domain.Proofs;

public sealed record DeliveryProof(
    Guid Id,
    Guid DeliveryId,
    Guid? StopId,
    ProofType Type,
    ProofStatus Status,
    string? RecipientName,
    bool OtpVerified,
    DateTimeOffset CreatedAt,
    Guid DriverId);

public sealed record ProofMedia(
    Guid Id,
    Guid ProofId,
    ProofMediaType MediaType,
    string ObjectKey,
    string MimeType,
    string Sha256,
    DateTimeOffset CapturedAt,
    long SizeBytes);

public enum ProofType { Pickup, Dropoff }
public enum ProofStatus { Draft, Verified, Rejected }
public enum ProofMediaType { Photo, Signature, Document }

public static class ProofValidationPolicy
{
    public static ProofStatus ResolveStatus(bool otpVerified, int mediaCount) =>
        otpVerified || mediaCount > 0 ? ProofStatus.Verified : ProofStatus.Rejected;
}
