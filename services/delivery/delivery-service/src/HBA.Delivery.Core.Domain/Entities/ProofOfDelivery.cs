using System.Security.Cryptography;
using HBA.Shared.Domain.Primitives;
using HBA.Shared.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>LA PREUVE DE REMISE — TROIS VALEURS QUI NE PROUVAIENT RIEN.</summary>
public sealed class ProofOfDelivery : ValueObject
{
    /// <summary>Longueur du code remis au destinataire.</summary>
    public const int PinLength = 4;

    private ProofOfDelivery(ProofOfDeliveryKind kind, string value, DateTime capturedAtUtc)
    {
        Kind = kind;
        Value = value;
        CapturedAtUtc = capturedAtUtc;
    }

    // Requis par EF Core.
    private ProofOfDelivery()
    {
        Value = string.Empty;
    }

    public ProofOfDeliveryKind Kind { get; private init; }

    /// <summary>Le code saisi, ou la référence du fichier téléversé.</summary>
    public string Value { get; private init; }

    public DateTime CapturedAtUtc { get; private init; }

    /// <summary>Émet un code à quatre chiffres.</summary>
    public static string IssuePin()
        => RandomNumberGenerator.GetInt32(0, 10_000).ToString("D" + PinLength);

    /// <summary>
    /// Enregistre une preuve. <paramref name="expectedPin"/> n'est utilisé que pour
    /// le genre Pin ; il est comparé à temps constant.
    /// </summary>
    public static Result<ProofOfDelivery> Capture(
        ProofOfDeliveryKind kind, string? value, string? expectedPin, DateTime atUtc)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (kind is ProofOfDeliveryKind.None)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.not_required", "Cette course n'exige aucune preuve de remise."));
        }

        if (trimmed is null)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.missing", "Une preuve de remise est requise pour cette course."));
        }

        return kind switch
        {
            ProofOfDeliveryKind.Pin => CapturePin(trimmed, expectedPin, atUtc),
            ProofOfDeliveryKind.Photo or ProofOfDeliveryKind.Signature => CaptureFile(kind, trimmed, atUtc),

            // Pas de « _ => une preuve quelconque ». Un genre ajouté à
            // l'énumération sans être traité ici doit CASSER — c'est exactement
            // l'oubli qui a rendu Photo et Signature décoratifs pendant des mois.
            _ => Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.kind_unsupported",
                    $"Le genre de preuve « {kind} » n'a pas de mode de capture défini."))
        };
    }

    private static Result<ProofOfDelivery> CapturePin(string submitted, string? expectedPin, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(expectedPin))
        {
            // La course exige un PIN mais aucun n'a été émis : c'est un défaut de
            // création, pas une erreur du livreur.
            return Result.Failure<ProofOfDelivery>(
                Error.Conflict("delivery.proof.pin_not_issued",
                    "Aucun code n'a été émis pour cette course : contactez le support."));
        }

        // Comparaison à TEMPS CONSTANT, comme pour une clé d'API. Sur quatre
        // chiffres l'écart de temps est infime, mais la règle ne souffre pas
        // d'exception : c'est en la relâchant « quand ça n'a pas d'importance »
        // qu'on la relâche ensuite là où elle en a.
        var expected = System.Text.Encoding.UTF8.GetBytes(expectedPin);
        var actual = System.Text.Encoding.UTF8.GetBytes(submitted);

        if (expected.Length != actual.Length || !CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.pin_mismatch",
                    "Le code saisi ne correspond pas à celui remis au destinataire."));
        }

        return new ProofOfDelivery(ProofOfDeliveryKind.Pin, submitted, atUtc);
    }

    /// <summary>
    /// Photo et signature : la valeur doit être une RÉFÉRENCE DE STOCKAGE, pas du
    /// texte.
    /// </summary>
    private static Result<ProofOfDelivery> CaptureFile(ProofOfDeliveryKind kind, string reference, DateTime atUtc)
    {
        var looksLikeReference =
            reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || reference.Contains('/', StringComparison.Ordinal);

        if (!looksLikeReference || reference.Length < 8)
        {
            return Result.Failure<ProofOfDelivery>(
                Error.Validation("delivery.proof.file_required",
                    kind is ProofOfDeliveryKind.Photo
                        ? "Une photo du colis remis est requise : téléversez-la d'abord, puis transmettez sa référence."
                        : "Une signature du destinataire est requise : téléversez-la d'abord, puis transmettez sa référence."));
        }

        return new ProofOfDelivery(kind, reference, atUtc);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Kind;
        yield return Value;
        yield return CapturedAtUtc;
    }
}
