namespace HBA.Media.Contracts;

/// <summary>Une représentation dérivée, telle qu'exposée (§12).</summary>
public sealed record MediaVariantView(
    string VariantType, string Url, int Width, int Height, long SizeBytes);

/// <summary>UN MÉDIA, VU DE L'EXTÉRIEUR DU MODULE.</summary>
/// <param name="Url"><summary>URL permanente. NULLE si le média n'est pas public.</summary></param>
public sealed record MediaView(
    Guid Id,
    string OwnerType,
    Guid OwnerId,
    string MediaType,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Visibility,
    string Status,
    int? Width,
    int? Height,
    string? Url,

    IReadOnlyList<MediaVariantView> Variants,
    DateTime CreatedOnUtc,

    // LE COMPTE QUI A DÉPOSÉ LE FICHIER — IL ÉTAIT PERSISTÉ ET INVISIBLE.
    Guid CreatedByUserId);

/// <summary>Une URL signée et sa durée de validité (§10).</summary>
public sealed record SignedMediaUrl(string Url, int ExpiresInSeconds);

/// <summary>L'API DU SERVICE MÉDIA, POUR LES AUTRES MODULES.</summary>
public interface IMediaModuleApi
{
    Task<MediaView?> GetAsync(Guid mediaId, CancellationToken cancellationToken = default);

    /// <summary>Plusieurs à la fois — une galerie produit en demande dix d'un coup.</summary>
    Task<IReadOnlyList<MediaView>> GetManyAsync(
        IReadOnlyList<Guid> mediaIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaView>> ListByOwnerAsync(
        string ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>URL de lecture temporaire pour un fichier privé (§10).</summary>
    Task<SignedMediaUrl?> CreateSignedUrlAsync(
        Guid mediaId, int expiresSeconds = 300, CancellationToken cancellationToken = default);
}
