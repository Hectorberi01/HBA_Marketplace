// DEPLACE DE `HBA.Shared.Infrastructure.Files` VERS LA COUCHE HTTP.

namespace HBA.Shared.Hosting.Http;

/// <summary>
/// Reconnaît le type RÉEL d'un fichier à ses premiers octets (« magic bytes »), au
/// lieu de croire ce que le client en dit.
/// </summary>
public static class FileSignature
{
    /// <summary>
    /// Nombre d'octets à lire pour reconnaître les formats supportés (WebP en exige
    /// 12).
    /// </summary>
    public const int HeaderBytes = 16;

    /// <summary>
    /// Type MIME réel du contenu, ou <c> null</c> si aucune signature connue ne
    /// correspond.
    /// </summary>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        // JPEG : FF D8 FF — commun à toutes les variantes (JFIF, Exif, SPIFF…).
        if (StartsWith(header, stackalloc byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return "image/jpeg";
        }

        // PNG : la signature complète sur 8 octets.
        if (StartsWith(header, stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        // WebP : conteneur RIFF. « RIFF » aux octets 0-3, « WEBP » aux octets 8-11
        // — les octets 4-7 portent la taille et varient.
        if (header.Length >= 12
            && StartsWith(header, "RIFF"u8)
            && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        // PDF : « %PDF- ».
        if (StartsWith(header, "%PDF-"u8))
        {
            return "application/pdf";
        }

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature)
        => header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);
}
