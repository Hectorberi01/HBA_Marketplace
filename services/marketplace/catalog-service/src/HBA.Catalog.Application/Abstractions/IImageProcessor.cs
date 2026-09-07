using HBA.Shared.Domain.Results;

namespace HBA.Catalog.Application.Abstractions;

/// <summary>Image traitée : contenu binaire prêt à stocker + type MIME.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType);

/// <summary>Marqueur de disponibilité RÉELLE du traitement d'image.</summary>
public interface IImageProcessingAvailability
{
    /// <summary>Faux quand le traitement est un no-op (service non configuré).</summary>
    bool IsAvailable { get; }
}

/// <summary>Port de traitement d'image (détourage IA + fond blanc).</summary>
public interface IImageProcessor
{
    /// <summary>
    /// Retire l'arrière-plan de l'image (IA) puis l'aplatit sur fond blanc (JPEG).
    /// </summary>
    Task<Result<ProcessedImage>> RemoveBackgroundWhiteAsync(
        string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);
}
