using Microsoft.AspNetCore.Http;
namespace HBA.Shared.Hosting.Http;

/// <summary>Résultat d'un contrôle d'upload.</summary>
/// <param name="Error">
/// Réponse 400 à renvoyer telle quelle, ou <c> null</c> si le fichier est valide.
/// </param>
/// <param name="ContentType">
/// Le type MIME RÉEL , déduit des octets du fichier — PAS celui déclaré par le
/// client.
/// </param>
public readonly record struct UploadCheck(IResult? Error, string? ContentType);

/// <summary>
/// Validation partagée des fichiers uploadés (multipart) : présence, taille max, et
/// vérification du type réel par les octets d'en-tête .
/// </summary>
public static class UploadValidation
{
    public const long MaxImageBytes = 5 * 1024 * 1024;     // 5 Mo
    public const long MaxDocumentBytes = 10 * 1024 * 1024; // 10 Mo

    public static readonly string[] ImageTypes = { "image/jpeg", "image/png", "image/webp" };
    public static readonly string[] DocumentTypes = { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    /// <summary>Valide un fichier uploadé et renvoie son type MIME RÉEL.</summary>
    public static async Task<UploadCheck> CheckAsync(
        IFormFile? file,
        long maxBytes,
        IReadOnlyCollection<string> allowedContentTypes,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return new UploadCheck(Results.BadRequest(new { error = "Fichier manquant ou vide." }), null);
        }

        if (file.Length > maxBytes)
        {
            return new UploadCheck(
                Results.BadRequest(new { error = $"Fichier trop volumineux (max {maxBytes / (1024 * 1024)} Mo)." }),
                null);
        }

        // Lecture des octets d'en-tête.
        var header = new byte[FileSignature.HeaderBytes];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        var actualType = FileSignature.Detect(header.AsSpan(0, read));

        // Aucune signature connue → REFUS. On n'accepte que ce qu'on sait
        // reconnaître : une liste noire (« tout sauf les .exe ») serait toujours
        // incomplète.
        if (actualType is null || !allowedContentTypes.Contains(actualType))
        {
            return new UploadCheck(
                Results.BadRequest(new
                {
                    error = "Type de fichier non autorisé. "
                          + $"Formats acceptés : {string.Join(", ", allowedContentTypes)}.",
                }),
                null);
        }

        // On renvoie le type RÉEL. L'appelant doit transmettre CELUI-CI au stockage
        // — jamais `file.ContentType`, qui reste la déclaration non vérifiée du
        // client.
        return new UploadCheck(null, actualType);
    }

    /// <summary>Valide une image (JPEG/PNG/WebP, ≤ 5 Mo) et renvoie son type réel.</summary>
    public static Task<UploadCheck> CheckImageAsync(IFormFile? file, CancellationToken cancellationToken = default)
        => CheckAsync(file, MaxImageBytes, ImageTypes, cancellationToken);

    /// <summary>
    /// Valide un document (image ou PDF, ≤ 10 Mo) et renvoie son type réel — pièces
    /// KYB, justificatifs, pièces jointes.
    /// </summary>
    public static Task<UploadCheck> CheckDocumentAsync(IFormFile? file, CancellationToken cancellationToken = default)
        => CheckAsync(file, MaxDocumentBytes, DocumentTypes, cancellationToken);
}
